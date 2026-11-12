using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shellboat.App.Services;
using Shellboat.Core.Connections;
using Shellboat.Core.Sessions;

namespace Shellboat.App.ViewModels;

/// <summary>Creates the SFTP channel for one connect attempt (SSH.NET in production, a fake in tests).</summary>
public delegate IFileChannel FileChannelFactory(ConnectionEntry connection, SshCredentials credentials);

/// <summary>
/// An SFTP tab for an SSH connection: this computer on the left (<see cref="Local"/>), the server on the right.
/// Download files/folders (to Downloads, a chosen folder, or the left pane), upload (picker, drop, or from the left
/// pane), all through one transfer queue (<c>SftpTabViewModel.Transfers.cs</c>). Same credential and host-key flow
/// as the terminal (<see cref="SshConnectFlow"/>).
/// </summary>
public sealed partial class SftpTabViewModel : SessionTabViewModel
{
    private readonly FileChannelFactory _channelFactory;
    private readonly CredentialResolver _credentials;
    private readonly IDialogService _dialogs;
    private readonly Func<string> _defaultFolder;
    private IFileChannel? _channel;
    private CancellationTokenSource? _connectCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoUp))]
    [NotifyCanExecuteChangedFor(nameof(UpCommand), nameof(UploadFilesCommand), nameof(UploadFolderCommand))]
    private string _currentPath = "";

    [ObservableProperty]
    private string _pathInput = "";

    [ObservableProperty]
    private bool _isTransferring;

    [ObservableProperty]
    private string _message = "";

    public SftpTabViewModel(
        ConnectionEntry connection,
        FileChannelFactory channelFactory,
        CredentialResolver credentials,
        IDialogService dialogs,
        Func<string>? defaultFolder = null)
        : base(connection)
    {
        _channelFactory = channelFactory;
        _credentials = credentials;
        _dialogs = dialogs;
        _defaultFolder = defaultFolder ?? LocalDownloads.DefaultFolder;
        Title = $"{connection.Name} — SFTP";
        Local = new LocalPaneViewModel(dialogs, m => Message = m, _defaultFolder());
        Local.Load();
        Transfers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTransfers));
        SelectedItems.CollectionChanged += (_, _) =>
        {
            DownloadCommand.NotifyCanExecuteChanged();
            DownloadToCommand.NotifyCanExecuteChanged();
        };
    }

    public override string IconKey => "Icon.Sftp";

    /// <summary>The left pane: this computer.</summary>
    public LocalPaneViewModel Local { get; }

    public ObservableCollection<RemoteFileItemViewModel> Items { get; } = [];

    /// <summary>Bound to the list's multi-selection.</summary>
    public ObservableCollection<RemoteFileItemViewModel> SelectedItems { get; } = [];

    public bool CanGoUp => RemotePath.Parent(CurrentPath) is not null;

    public override async Task ConnectAsync()
    {
        if (State == SessionState.Connected)
        {
            return;
        }

        await TearDownAsync();
        _connectCts = new CancellationTokenSource();
        var ct = _connectCts.Token;
        State = SessionState.Connecting;
        Error = null;

        var result = await SshConnectFlow.ConnectAsync(
            Connection,
            _credentials,
            creds => _channelFactory(Connection, creds),
            (channel, token) => channel.ConnectAsync(token),
            channel => channel.DisposeAsync(),
            ct);

        if (result.Channel is not { } channel)
        {
            Error = result.Error ?? "Connection failed.";
            State = SessionState.Failed;
            if (result.HostKeyRejected)
            {
                await _dialogs.ShowMessageAsync("Host key rejected", Error);
            }

            return;
        }

        _channel = channel;
        State = SessionState.Connected;
        await NavigateAsync(string.IsNullOrEmpty(CurrentPath) ? channel.HomeDirectory : CurrentPath);
    }

    [RelayCommand]
    public async Task NavigateAsync(string? path)
    {
        if (_channel is not { } channel || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        path = path.Trim();
        try
        {
            var entries = await channel.ListAsync(path, CancellationToken.None);
            Items.Clear();
            SelectedItems.Clear();
            foreach (var e in entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                Items.Add(new RemoteFileItemViewModel(e));
            }

            CurrentPath = path;
            PathInput = path;
            Message = $"{Items.Count} item(s)";
        }
        catch (Exception ex)
        {
            PathInput = CurrentPath;
            ReportFailure(ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private Task UpAsync() => NavigateAsync(RemotePath.Parent(CurrentPath));

    [RelayCommand]
    private Task RefreshAsync() => NavigateAsync(CurrentPath);

    /// <summary>Double-click / Enter on a row: open a folder, download a file to the default folder.</summary>
    [RelayCommand]
    private Task OpenAsync(RemoteFileItemViewModel? item) => item switch
    {
        null => Task.CompletedTask,
        { IsDirectory: true } => NavigateAsync(item.Entry.FullPath),
        _ => DownloadItemsAsync([item], _defaultFolder()),
    };

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task DownloadAsync() => DownloadItemsAsync([.. SelectedItems], _defaultFolder());

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadToAsync()
    {
        var folder = await _dialogs.PickFolderAsync("Download to…");
        if (folder is not null)
        {
            await DownloadItemsAsync([.. SelectedItems], folder);
        }
    }

    private bool CanDownload() => SelectedItems.Count > 0;


    // ---- Row context menu. Each acts on the right-clicked row, or on the whole selection when that row is part of it.

    [RelayCommand]
    private Task DownloadItemAsync(RemoteFileItemViewModel? item)
        => item is null ? Task.CompletedTask : DownloadItemsAsync(Targets(item), _defaultFolder());

    [RelayCommand]
    private async Task DownloadItemToAsync(RemoteFileItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var targets = Targets(item);
        var folder = await _dialogs.PickFolderAsync("Download to…");
        if (folder is not null)
        {
            await DownloadItemsAsync(targets, folder);
        }
    }

    [RelayCommand]
    private async Task CopyPathAsync(RemoteFileItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var paths = Targets(item).Select(i => i.Entry.FullPath).ToList();
        await _dialogs.SetClipboardTextAsync(string.Join("\n", paths));
        Message = paths.Count == 1 ? $"Copied {paths[0]}" : $"Copied {paths.Count} paths";
    }

    [RelayCommand]
    private async Task RenameAsync(RemoteFileItemViewModel? item)
    {
        if (item is null || _channel is not { } channel)
        {
            return;
        }

        var input = await _dialogs.PromptTextAsync("Rename", $"New name for \"{item.Name}\"", item.Name);
        if (input is null || input == item.Name)
        {
            return;
        }

        if (ValidateNewName(input) is { } problem)
        {
            await _dialogs.ShowMessageAsync("Rename", problem);
            return;
        }

        var directory = RemotePath.Parent(item.Entry.FullPath) ?? CurrentPath;
        try
        {
            await channel.RenameAsync(item.Entry.FullPath, RemotePath.Combine(directory, input), CancellationToken.None);
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
            return;
        }

        await NavigateAsync(CurrentPath);
        Message = $"Renamed {item.Name} → {input}";
    }

    [RelayCommand]
    private async Task PropertiesAsync(RemoteFileItemViewModel? item)
    {
        if (item is null || _channel is not { } channel)
        {
            return;
        }

        RemoteFileProperties props;
        try
        {
            props = await channel.GetPropertiesAsync(item.Entry.FullPath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
            return;
        }

        await _dialogs.ShowMessageAsync($"Properties — {item.Name}", FormatProperties(item.Entry, props));
    }

    /// <summary>Null when <paramref name="name"/> is a valid single file name; otherwise why not.</summary>
    public static string? ValidateNewName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "The name can't be empty.";
        }

        if (name is "." or "..")
        {
            return "\".\" and \"..\" aren't valid names.";
        }

        if (name.Contains('/') || name.Contains('\0'))
        {
            return "The name can't contain \"/\". Rename only changes the name, not the folder.";
        }

        return null;
    }

    public static string FormatProperties(RemoteFileEntry entry, RemoteFileProperties p)
    {
        var kind = entry.IsSymbolicLink
            ? (p.IsDirectory ? "Link to folder" : "Link to file")
            : p.IsDirectory ? "Folder" : "File";
        var lines = new List<string>
        {
            $"Path:  {p.FullPath}",
            $"Type:  {kind}",
        };
        if (!p.IsDirectory)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Size:  {RemoteFileItemViewModel.FormatSize(p.Size)} ({p.Size:N0} bytes)"));
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture, $"Modified:  {p.Modified.ToLocalTime():yyyy-MM-dd HH:mm:ss}"));
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"Accessed:  {p.Accessed.ToLocalTime():yyyy-MM-dd HH:mm:ss}"));
        lines.Add($"Permissions:  {RemoteFileItemViewModel.FormatMode(p.Mode, p.IsDirectory, entry.IsSymbolicLink)}");
        lines.Add($"Owner:  uid {p.UserId}, gid {p.GroupId}");
        return string.Join("\n", lines);
    }

    [RelayCommand]
    private async Task NewFolderAsync()
    {
        if (_channel is not { } channel || CurrentPath.Length == 0)
        {
            return;
        }

        var input = await _dialogs.PromptTextAsync("New folder", $"Name of the new folder in {CurrentPath}", "New folder");
        if (input is null)
        {
            return;
        }

        if (ValidateNewName(input) is { } problem)
        {
            await _dialogs.ShowMessageAsync("New folder", problem);
            return;
        }

        try
        {
            await channel.CreateDirectoryAsync(RemotePath.Combine(CurrentPath, input), CancellationToken.None);
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
            return;
        }

        await NavigateAsync(CurrentPath);
        Message = $"Created {input}";
    }

    public IReadOnlyList<RemoteFileItemViewModel> Targets(RemoteFileItemViewModel item)
        => SelectedItems.Contains(item) ? [.. SelectedItems] : [item];



    private void ReportFailure(Exception ex)
    {
        if (_channel is { IsConnected: false })
        {
            Error = $"Connection lost: {ex.Message}";
            State = SessionState.Disconnected;
            return;
        }

        Message = ex.Message;
    }

    private async Task TearDownAsync()
    {
        CancelAllTransfers();
        _connectCts?.Cancel();
        _connectCts?.Dispose();
        _connectCts = null;
        var channel = _channel;
        _channel = null;
        if (channel is not null)
        {
            await channel.DisposeAsync();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await TearDownAsync();
    }
}
