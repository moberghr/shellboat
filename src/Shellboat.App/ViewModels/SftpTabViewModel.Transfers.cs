using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Shellboat.App.Services;

namespace Shellboat.App.ViewModels;

/// <summary>
/// Uploads and downloads of the SFTP tab. Every transfer becomes a <see cref="TransferViewModel"/> in
/// <see cref="Transfers"/> and runs through one queue, one at a time (SFTP over one session; a second drop while a
/// transfer runs simply waits its turn). The returned task of each entry point completes when that transfer ended.
/// </summary>
public sealed partial class SftpTabViewModel
{
    private readonly Queue<TransferJob> _queue = new();
    private bool _pumping;
    private TransferViewModel? _current;

    private sealed record TransferJob(TransferViewModel Transfer, string Verb, Func<TransferViewModel, CancellationToken, Task<string>> Work, TaskCompletionSource Done);

    /// <summary>Newest first: queued, running and finished transfers with their progress and outcome.</summary>
    public ObservableCollection<TransferViewModel> Transfers { get; } = [];

    public bool HasTransfers => Transfers.Count > 0;

    /// <summary>The most recent finished download (for tests).</summary>
    public DownloadSummary? LastDownload { get; private set; }

    /// <summary>The most recent finished upload (for tests).</summary>
    public UploadSummary? LastUpload { get; private set; }

    private bool CanUpload() => CurrentPath.Length > 0;

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadFilesAsync()
    {
        var files = await _dialogs.PickFilesAsync($"Upload to {CurrentPath}");
        await UploadPathsAsync(files);
    }

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadFolderAsync()
    {
        var folder = await _dialogs.PickFolderAsync($"Upload folder to {CurrentPath}");
        if (folder is not null)
        {
            await UploadPathsAsync([folder]);
        }
    }

    /// <summary>Uploads local files/folders (picker or drop) into the folder being shown.</summary>
    public Task UploadPathsAsync(IReadOnlyList<string> localPaths) => UploadPathsToAsync(localPaths, CurrentPath);

    /// <summary>Uploads into <paramref name="remoteFolder"/>; refreshes the list when that is the folder being shown.
    /// Taken names get a " (1)" suffix — nothing on the server is overwritten.</summary>
    public Task UploadPathsToAsync(IReadOnlyList<string> localPaths, string remoteFolder)
    {
        if (_channel is not { } channel || localPaths.Count == 0 || remoteFolder.Length == 0)
        {
            return Task.CompletedTask;
        }

        var title = localPaths.Count == 1 ? Path.GetFileName(Path.TrimEndingDirectorySeparator(localPaths[0])) : $"{localPaths.Count} items";
        var from = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(localPaths[0])) ?? "";
        var transfer = new TransferViewModel(TransferDirection.Upload, title, from, remoteFolder);
        return Enqueue(transfer, "Upload", async (t, ct) =>
        {
            (t.TotalFiles, t.TotalBytes) = await Task.Run(() => LocalFiles.Totals(localPaths), ct);
            var progress = new Progress<UploadProgress>(p =>
            {
                Track(t, p.CurrentName, p.FilesDone, p.BytesDone);
                Message = $"Uploading {p.CurrentName} — {p.FilesDone} file(s), {RemoteFileItemViewModel.FormatSize(p.BytesDone)}";
            });
            UploadSummary summary;
            try
            {
                summary = await new SftpUploader(channel).UploadAsync(localPaths, remoteFolder, progress, ct);
            }
            finally
            {
                await RefreshRemoteIfShowingAsync(remoteFolder);
            }

            LastUpload = summary;
            Track(t, "", summary.Files, summary.Bytes);
            var skipped = summary.SkippedLinks > 0 ? $" ({summary.SkippedLinks} linked folder(s) not followed)" : "";
            return $"Uploaded {summary.Files} file(s), {RemoteFileItemViewModel.FormatSize(summary.Bytes)} to {remoteFolder}{skipped}";
        });
    }

    /// <summary>Left pane row menu "Upload": the row (or the selection it belongs to) into the folder being shown.</summary>
    [RelayCommand]
    private Task UploadLocalItemAsync(LocalFileItemViewModel? item)
        => item is null ? Task.CompletedTask : UploadPathsAsync([.. Local.Targets(item).Select(i => i.Entry.FullPath)]);

    /// <summary>Right pane row menu "Download to left pane": into the folder the left pane shows.</summary>
    [RelayCommand]
    private Task DownloadItemToLocalAsync(RemoteFileItemViewModel? item)
        => item is null ? Task.CompletedTask : DownloadItemsAsync(Targets(item), Local.CurrentPath);

    /// <summary>Left-pane rows dropped on the right pane: into the folder row they were dropped on, else the folder shown.</summary>
    public Task DropOnRemoteAsync(IReadOnlyList<LocalFileItemViewModel> items, RemoteFileItemViewModel? onto)
        => UploadPathsToAsync([.. items.Select(i => i.Entry.FullPath)], onto is { IsDirectory: true } ? onto.Entry.FullPath : CurrentPath);

    /// <summary>Files from the OS file manager dropped on the right pane.</summary>
    public Task DropPathsOnRemoteAsync(IReadOnlyList<string> paths, RemoteFileItemViewModel? onto)
        => UploadPathsToAsync(paths, onto is { IsDirectory: true } ? onto.Entry.FullPath : CurrentPath);

    /// <summary>Right-pane rows dropped on the left pane: into the folder row they were dropped on, else the folder shown.</summary>
    public Task DropOnLocalAsync(IReadOnlyList<RemoteFileItemViewModel> items, LocalFileItemViewModel? onto)
        => DownloadItemsAsync(items, onto is { IsDirectory: true } ? onto.Entry.FullPath : Local.CurrentPath);

    [RelayCommand]
    private void CancelTransfer() => _current?.Cancel();

    [RelayCommand]
    private void ClearFinishedTransfers()
    {
        foreach (var t in Transfers.Where(t => !t.IsActive).ToList())
        {
            Transfers.Remove(t);
        }
    }

    private Task DownloadItemsAsync(IReadOnlyList<RemoteFileItemViewModel> items, string folder)
    {
        if (_channel is not { } channel || items.Count == 0 || folder.Length == 0)
        {
            return Task.CompletedTask;
        }

        var entries = items.Select(i => i.Entry).ToList();
        var title = entries.Count == 1 ? entries[0].Name : $"{entries.Count} items";
        var transfer = new TransferViewModel(TransferDirection.Download, title, RemotePath.Parent(entries[0].FullPath) ?? CurrentPath, folder);
        return Enqueue(transfer, "Download", async (t, ct) =>
        {
            (t.TotalFiles, t.TotalBytes) = await SftpDownloader.TotalsAsync(channel, entries, ct);
            var progress = new Progress<DownloadProgress>(p =>
            {
                Track(t, p.CurrentName, p.FilesDone, p.BytesDone);
                Message = $"Downloading {p.CurrentName} — {p.FilesDone} file(s), {RemoteFileItemViewModel.FormatSize(p.BytesDone)}";
            });
            DownloadSummary summary;
            try
            {
                summary = await new SftpDownloader(channel).DownloadAsync(entries, folder, progress, ct);
            }
            finally
            {
                RefreshLocalIfShowing(folder);
            }

            LastDownload = summary;
            Track(t, "", summary.Files, summary.Bytes);
            var skipped = summary.SkippedLinks > 0 ? $" ({summary.SkippedLinks} linked folder(s) not followed)" : "";
            return $"Downloaded {summary.Files} file(s), {RemoteFileItemViewModel.FormatSize(summary.Bytes)} to {folder}{skipped}";
        });
    }

    private static void Track(TransferViewModel t, string name, int files, long bytes)
    {
        t.CurrentName = name;
        t.FilesDone = files;
        t.BytesDone = bytes;
    }

    private async Task RefreshRemoteIfShowingAsync(string folder)
    {
        if (_channel is { IsConnected: true } && CurrentPath == folder)
        {
            var message = Message;
            await NavigateAsync(folder);
            Message = message;
        }
    }

    private void RefreshLocalIfShowing(string folder)
    {
        if (string.Equals(Path.TrimEndingDirectorySeparator(Local.CurrentPath), Path.TrimEndingDirectorySeparator(folder), StringComparison.Ordinal))
        {
            Local.Navigate(Local.CurrentPath);
        }
    }

    private Task Enqueue(TransferViewModel transfer, string verb, Func<TransferViewModel, CancellationToken, Task<string>> work)
    {
        var job = new TransferJob(transfer, verb, work, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        Transfers.Insert(0, transfer);
        bool start;
        lock (_queue)
        {
            _queue.Enqueue(job);
            start = !_pumping;
            _pumping = true;
        }

        if (start)
        {
            IsTransferring = true;
            _ = PumpAsync();
        }

        return job.Done.Task;
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            TransferJob? job;
            lock (_queue)
            {
                if (!_queue.TryDequeue(out job))
                {
                    _pumping = false;
                    break;
                }
            }

            await RunAsync(job);

            bool last;
            lock (_queue)
            {
                last = _queue.Count == 0;
                if (last)
                {
                    _pumping = false;
                }
            }

            if (last)
            {
                IsTransferring = false;
            }

            // Completed only after IsTransferring settled, so an awaiting caller sees the final state.
            job.Done.TrySetResult();
            if (last)
            {
                return;
            }
        }

        IsTransferring = false;
    }

    private async Task RunAsync(TransferJob job)
    {
        var t = job.Transfer;
        if (t.Token.IsCancellationRequested)
        {
            t.Finish(TransferState.Cancelled, "");
            return;
        }

        _current = t;
        t.Start();
        try
        {
            var line = await job.Work(t, t.Token);
            t.Finish(TransferState.Done, line);
            Message = line;
        }
        catch (OperationCanceledException)
        {
            t.Finish(TransferState.Cancelled, "");
            Message = $"{job.Verb} cancelled.";
        }
        catch (Exception ex)
        {
            t.Finish(TransferState.Failed, ex.Message);
            ReportFailure(ex);
        }
        finally
        {
            _current = null;
        }
    }

    private void CancelAllTransfers()
    {
        foreach (var t in Transfers.Where(t => t.IsActive))
        {
            t.Cancel();
        }
    }
}
