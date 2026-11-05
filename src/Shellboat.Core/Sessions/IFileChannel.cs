namespace Shellboat.Core.Sessions;

/// <summary>One entry of a remote directory listing. <see cref="Name"/> is exactly what the server sent —
/// never use it as a local path without sanitising it.</summary>
public sealed record RemoteFileEntry(string Name, string FullPath, bool IsDirectory, bool IsSymbolicLink, long Size, DateTimeOffset Modified);

/// <summary>What "Properties" shows. <see cref="Mode"/> holds the Unix permission bits (e.g. 0755 plus setuid/setgid/sticky).</summary>
public sealed record RemoteFileProperties(
    string FullPath, bool IsDirectory, bool IsSymbolicLink, long Size, DateTimeOffset Modified, DateTimeOffset Accessed, int Mode, int UserId, int GroupId);

/// <summary>
/// A remote file system over SFTP. Same host-key trust and authentication as <see cref="IShellChannel"/>;
/// the SSH implementation lives in Shellboat.Ssh, the file browser talks only to this.
/// </summary>
public interface IFileChannel : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct);

    /// <summary>The directory the server put us in at login (usually the user's home).</summary>
    string HomeDirectory { get; }

    /// <summary>False once the session dropped; the browser then offers a reconnect.</summary>
    bool IsConnected { get; }

    Task<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken ct);

    Task<RemoteFileProperties> GetPropertiesAsync(string path, CancellationToken ct);

    /// <summary>Renames or moves <paramref name="fromPath"/>. Fails when <paramref name="toPath"/> already exists — never overwrites.</summary>
    Task RenameAsync(string fromPath, string toPath, CancellationToken ct);

    /// <param name="progress">Total bytes copied so far.</param>
    Task DownloadAsync(string remotePath, Stream destination, IProgress<long>? progress, CancellationToken ct);
}
