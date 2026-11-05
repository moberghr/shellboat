using Shellboat.Core.Sessions;

namespace Shellboat.App.Services;

/// <summary>Progress of a running download: the file being copied, files finished, bytes copied overall.</summary>
public sealed record DownloadProgress(string CurrentName, int FilesDone, long BytesDone);

/// <summary>What a finished download did. <see cref="SkippedLinks"/> = symlinked folders not followed.</summary>
public sealed record DownloadSummary(int Files, long Bytes, int SkippedLinks, IReadOnlyList<string> SavedPaths);

/// <summary>
/// Copies remote files and folders (recursively) into a local folder. Names are sanitised and never overwrite
/// (see <see cref="LocalDownloads"/>); a file that fails or is cancelled half-way is deleted, not left truncated.
/// Symlinked folders are not followed, so a link loop can't make it run forever.
/// </summary>
public sealed class SftpDownloader(IFileChannel channel)
{
    private int _files;
    private long _bytes;
    private int _skippedLinks;
    private readonly List<string> _saved = [];

    public async Task<DownloadSummary> DownloadAsync(IReadOnlyList<RemoteFileEntry> items, string localFolder, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(localFolder);
        foreach (var item in items)
        {
            await CopyAsync(item, localFolder, progress, ct);
        }

        return new DownloadSummary(_files, _bytes, _skippedLinks, _saved);
    }

    private async Task CopyAsync(RemoteFileEntry item, string localFolder, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (item.IsDirectory)
        {
            if (item.IsSymbolicLink)
            {
                _skippedLinks++;
                return;
            }

            var dir = LocalDownloads.UniquePath(localFolder, item.Name);
            Directory.CreateDirectory(dir);
            _saved.Add(dir);
            foreach (var child in await channel.ListAsync(item.FullPath, ct))
            {
                await CopyAsync(child, dir, progress, ct);
            }

            return;
        }

        var path = LocalDownloads.UniquePath(localFolder, item.Name);
        var before = _bytes;
        progress?.Report(new DownloadProgress(item.Name, _files, _bytes));
        try
        {
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var fileProgress = progress is null ? null : new InlineProgress<long>(n => progress.Report(new DownloadProgress(item.Name, _files, before + n)));
                await channel.DownloadAsync(item.FullPath, file, fileProgress, ct);
                _bytes = before + file.Length;
            }
        }
        catch
        {
            TryDelete(path);
            throw;
        }

        _files++;
        _saved.Add(path);
        progress?.Report(new DownloadProgress(item.Name, _files, _bytes));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort: the caller reports the original failure.
        }
    }

    /// <summary>Forwards immediately on the reporting thread (the outer progress decides where it lands).</summary>
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
