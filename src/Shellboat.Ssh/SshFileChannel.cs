using Renci.SshNet;
using Shellboat.Core.Connections;
using Shellboat.Core.Sessions;
using Shellboat.Core.Trust;

namespace Shellboat.Ssh;

/// <summary>An SFTP session (SSH.NET <see cref="SftpClient"/>) with the same login and host-key rules as the shell.</summary>
public sealed class SshFileChannel : IFileChannel
{
    private readonly SshLogin _login;
    private SftpClient? _client;

    public SshFileChannel(ConnectionEntry connection, SshCredentials credentials, ITrustStore trust, ITrustPrompter prompter)
    {
        _login = new SshLogin(connection, credentials, trust, prompter);
    }

    public string HomeDirectory { get; private set; } = "/";

    public bool IsConnected => _client?.IsConnected == true;

    public async Task ConnectAsync(CancellationToken ct)
    {
        var client = await _login.ConnectAsync(info => new SftpClient(info), ct).ConfigureAwait(false);
        _client = client;
        HomeDirectory = string.IsNullOrEmpty(client.WorkingDirectory) ? "/" : client.WorkingDirectory;
    }

    public async Task<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken ct)
    {
        var client = Client();
        var entries = new List<RemoteFileEntry>();
        await foreach (var f in client.ListDirectoryAsync(path, ct).ConfigureAwait(false))
        {
            if (f.Name is "." or "..")
            {
                continue;
            }

            var isDirectory = f.IsDirectory;
            if (f.IsSymbolicLink)
            {
                // The listing describes the link itself; ask what it points at so linked folders open as folders.
                try
                {
                    isDirectory = (await client.GetAttributesAsync(f.FullName, ct).ConfigureAwait(false)).IsDirectory;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Dangling link: show it as a file; a download attempt reports the error.
                }
            }

            entries.Add(new RemoteFileEntry(f.Name, f.FullName, isDirectory, f.IsSymbolicLink, f.Length, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero)));
        }

        return entries;
    }

    public async Task<RemoteFileProperties> GetPropertiesAsync(string path, CancellationToken ct)
    {
        var a = await Client().GetAttributesAsync(path, ct).ConfigureAwait(false);
        var mode = Bit(a.OwnerCanRead, 0x100) | Bit(a.OwnerCanWrite, 0x80) | Bit(a.OwnerCanExecute, 0x40)
            | Bit(a.GroupCanRead, 0x20) | Bit(a.GroupCanWrite, 0x10) | Bit(a.GroupCanExecute, 0x8)
            | Bit(a.OthersCanRead, 0x4) | Bit(a.OthersCanWrite, 0x2) | Bit(a.OthersCanExecute, 0x1)
            | Bit(a.IsUIDBitSet, 0x800) | Bit(a.IsGroupIDBitSet, 0x400) | Bit(a.IsStickyBitSet, 0x200);
        return new RemoteFileProperties(
            path,
            a.IsDirectory,
            a.IsSymbolicLink,
            a.Size,
            new DateTimeOffset(a.LastWriteTimeUtc, TimeSpan.Zero),
            new DateTimeOffset(a.LastAccessTimeUtc, TimeSpan.Zero),
            mode,
            a.UserId,
            a.GroupId);

        static int Bit(bool set, int value) => set ? value : 0;
    }

    public async Task RenameAsync(string fromPath, string toPath, CancellationToken ct)
    {
        var client = Client();
        // Plain SFTP rename already refuses an existing target on OpenSSH, but servers differ; check so a rename
        // can never silently replace a file.
        if (await client.ExistsAsync(toPath, ct).ConfigureAwait(false))
        {
            throw new IOException($"\"{toPath}\" already exists.");
        }

        await client.RenameFileAsync(fromPath, toPath, ct).ConfigureAwait(false);
    }

    public Task DownloadAsync(string remotePath, Stream destination, IProgress<long>? progress, CancellationToken ct)
    {
        var report = progress is null ? null : new Progress<DownloadFileProgressReport>(r => progress.Report((long)r.TotalBytesDownloaded));
        return Client().DownloadFileAsync(remotePath, destination, report, ct);
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (_client?.IsConnected == true)
            {
                _client.Disconnect();
            }

            _client?.Dispose();
        }
        catch (Exception)
        {
            // Best-effort teardown.
        }

        return ValueTask.CompletedTask;
    }

    private SftpClient Client() => _client ?? throw new InvalidOperationException("Not connected.");
}
