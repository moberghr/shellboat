using Shellboat.App.Services;
using Shellboat.App.ViewModels;
using Shellboat.Core.Connections;
using Shellboat.Core.Secrets;
using Shellboat.Core.Sessions;
using Shellboat.Core.Trust;
using Shellboat.Rdp;

namespace Shellboat.App.Tests;

internal sealed class FakeSecrets(bool canStore = true) : ISecretStore
{
    public Dictionary<(Guid, SecretPurpose), string> Items { get; } = [];

    public bool CanStore { get; } = canStore;

    public Task SetAsync(Guid id, SecretPurpose p, string s, CancellationToken ct)
    {
        if (!CanStore)
        {
            throw new SecretStorageRefusedException("no");
        }

        Items[(id, p)] = s;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(Guid id, SecretPurpose p, CancellationToken ct)
        => Task.FromResult(Items.TryGetValue((id, p), out var v) ? v : null);

    public Task DeleteAsync(Guid id, SecretPurpose p, CancellationToken ct)
    {
        Items.Remove((id, p));
        return Task.CompletedTask;
    }
}

internal sealed class FakeDialogs : IDialogService
{
    public Queue<SecretPromptResult?> SecretAnswers { get; } = new();
    public List<string> SecretPrompts { get; } = [];
    public List<string> Messages { get; } = [];
    public bool ConfirmAnswer { get; set; } = true;

    public Task<SecretPromptResult?> PromptSecretAsync(string title, string message, bool canRemember)
    {
        SecretPrompts.Add(message);
        return Task.FromResult(SecretAnswers.Count > 0 ? SecretAnswers.Dequeue() : null);
    }

    public Task<bool> ConfirmAsync(string title, string message, string acceptLabel) => Task.FromResult(ConfirmAnswer);

    public Task ShowMessageAsync(string title, string message)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }

    public Queue<string?> TextAnswers { get; } = new();

    public Task<string?> PromptTextAsync(string title, string label, string initial)
        => Task.FromResult(TextAnswers.Count > 0 ? TextAnswers.Dequeue() : null);

    public string? Clipboard { get; private set; }

    public Task SetClipboardTextAsync(string text)
    {
        Clipboard = text;
        return Task.CompletedTask;
    }

    /// <summary>Applied to the settings editor before "saving"; null = the user cancels.</summary>
    public Action<SettingsViewModel>? SettingsChoice { get; set; }

    public Task<bool> EditSettingsAsync(SettingsViewModel settings)
    {
        SettingsChoice?.Invoke(settings);
        return Task.FromResult(SettingsChoice is not null);
    }

    /// <summary>Stands in for the user copying something themselves.</summary>
    public void UserCopies(string text) => Clipboard = text;

    public Task ClearClipboardIfAsync(string text)
    {
        if (Clipboard == text)
        {
            Clipboard = null;
        }

        return Task.CompletedTask;
    }

    public Task<bool> ConfirmHostKeyAsync(TrustPrompt prompt) => Task.FromResult(ConfirmAnswer);

    public Task<CertificateTrustDecision> ConfirmRdpCertificateAsync(RdpCertificatePrompt prompt) => Task.FromResult(CertificateTrustDecision.Reject);

    public Task<bool> EditConnectionAsync(ConnectionEditorViewModel editor) => Task.FromResult(false);

    public Task ShowKeePassImportAsync(KeePassImportViewModel import) => Task.CompletedTask;

    /// <summary>Applied to the picker before "saving"; null = the user cancels.</summary>
    public Action<IconPickerViewModel>? IconChoice { get; set; }

    public IconPickerViewModel? LastPicker { get; private set; }

    public Task<bool> PickIconAsync(IconPickerViewModel picker)
    {
        LastPicker = picker;
        IconChoice?.Invoke(picker);
        return Task.FromResult(IconChoice is not null);
    }

    public string? PickedFile { get; set; }

    public Task<string?> PickFileAsync(string title, string fileTypeName, IReadOnlyList<string> patterns) => Task.FromResult(PickedFile);

    public string? PickedFolder { get; set; }

    public Task<string?> PickFolderAsync(string title) => Task.FromResult(PickedFolder);
}

/// <summary>A shell channel whose connect outcome is scripted per attempt.</summary>
internal sealed class FakeChannel(Func<SshCredentials, Exception?> outcome, SshCredentials creds) : IShellChannel
{
    public List<byte[]> Written { get; } = [];
    public List<(int, int)> Resizes { get; } = [];
    public bool Disposed { get; private set; }
    public SshCredentials Credentials { get; } = creds;

    public event Action<byte[]>? DataReceived;
    public event Action<string?>? Closed;

    public Task ConnectAsync(int cols, int rows, CancellationToken ct)
        => outcome(Credentials) is { } ex ? Task.FromException(ex) : Task.CompletedTask;

    public void Write(byte[] data) => Written.Add(data);

    public void Resize(int cols, int rows) => Resizes.Add((cols, rows));

    public void Emit(string text) => DataReceived?.Invoke(System.Text.Encoding.UTF8.GetBytes(text));

    public void Close(string? reason) => Closed?.Invoke(reason);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class MemoryConnectionStore : IConnectionStore
{
    public ConnectionTreeDocument Saved { get; private set; } = ConnectionTreeDocument.Empty;

    public int Saves { get; private set; }

    public Task<ConnectionTreeDocument> LoadAsync(CancellationToken ct) => Task.FromResult(Saved);

    public Task SaveAsync(ConnectionTreeDocument document, CancellationToken ct)
    {
        Saved = document;
        Saves++;
        return Task.CompletedTask;
    }
}

/// <summary>An in-memory SFTP server: directories by path, file contents by path.</summary>
internal sealed class FakeFileChannel(Func<SshCredentials, Exception?> outcome, SshCredentials creds) : IFileChannel
{
    public Dictionary<string, List<RemoteFileEntry>> Directories { get; } = [];
    public Dictionary<string, byte[]> Files { get; } = [];
    public SshCredentials Credentials { get; } = creds;
    public bool Disposed { get; private set; }

    /// <summary>When set, downloading this path writes half the file and then throws.</summary>
    public string? FailDownloadOf { get; set; }

    public string HomeDirectory { get; set; } = "/home/u";

    public bool IsConnected { get; set; } = true;

    public Task ConnectAsync(CancellationToken ct)
        => outcome(Credentials) is { } ex ? Task.FromException(ex) : Task.CompletedTask;

    public void AddDir(string path, bool isLink = false)
    {
        Directories.TryAdd(path, []);
        AddEntry(path, new RemoteFileEntry(Name(path), path, true, isLink, 0, DateTimeOffset.UnixEpoch));
    }

    /// <summary>Adds a file. <paramref name="hostileName"/> = the name the server reports instead of the real one.</summary>
    public void AddFile(string path, string content, string? hostileName = null)
    {
        Files[path] = System.Text.Encoding.UTF8.GetBytes(content);
        AddEntry(path, new RemoteFileEntry(hostileName ?? Name(path), path, false, false, Files[path].Length, DateTimeOffset.UnixEpoch));
    }

    public Task<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken ct)
    {
        if (!IsConnected)
        {
            throw new IOException("connection dropped");
        }

        return Directories.TryGetValue(path, out var list)
            ? Task.FromResult<IReadOnlyList<RemoteFileEntry>>(list)
            : Task.FromException<IReadOnlyList<RemoteFileEntry>>(new IOException($"No such file: {path}"));
    }

    public Dictionary<string, RemoteFileProperties> Properties { get; } = [];

    public Task<RemoteFileProperties> GetPropertiesAsync(string path, CancellationToken ct)
        => Properties.TryGetValue(path, out var p)
            ? Task.FromResult(p)
            : Task.FromException<RemoteFileProperties>(new IOException($"No such file: {path}"));

    public List<(string From, string To)> Renames { get; } = [];

    public Task RenameAsync(string fromPath, string toPath, CancellationToken ct)
    {
        var parent = Shellboat.App.Services.RemotePath.Parent(fromPath) ?? "/";
        var list = Directories[parent];
        if (list.Any(e => e.FullPath == toPath))
        {
            return Task.FromException(new IOException($"\"{toPath}\" already exists."));
        }

        var index = list.FindIndex(e => e.FullPath == fromPath);
        list[index] = list[index] with { Name = Name(toPath), FullPath = toPath };
        Renames.Add((fromPath, toPath));
        return Task.CompletedTask;
    }

    public async Task DownloadAsync(string remotePath, Stream destination, IProgress<long>? progress, CancellationToken ct)
    {
        var data = Files[remotePath];
        if (remotePath == FailDownloadOf)
        {
            await destination.WriteAsync(data.AsMemory(0, data.Length / 2), ct);
            throw new IOException("transfer failed");
        }

        await destination.WriteAsync(data, ct);
        progress?.Report(data.Length);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    private void AddEntry(string path, RemoteFileEntry entry)
    {
        var parent = Shellboat.App.Services.RemotePath.Parent(path) ?? "/";
        if (!Directories.TryGetValue(parent, out var list))
        {
            Directories[parent] = list = [];
        }

        list.Add(entry);
    }

    private static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];
}
