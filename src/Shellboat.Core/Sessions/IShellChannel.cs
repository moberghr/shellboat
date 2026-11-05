namespace Shellboat.Core.Sessions;

/// <summary>
/// An interactive remote shell with a PTY: bytes in, bytes out, resizable. The SSH implementation lives in
/// Shellboat.Ssh; the UI talks only to this, so terminal tabs are testable with a fake.
/// </summary>
public interface IShellChannel : IAsyncDisposable
{
    /// <summary>Raised on a background thread with output from the remote end.</summary>
    event Action<byte[]>? DataReceived;

    /// <summary>Raised once when the channel ends. <c>null</c> reason = clean exit (the shell logged out).</summary>
    event Action<string?>? Closed;

    Task ConnectAsync(int cols, int rows, CancellationToken ct);

    void Write(byte[] data);

    void Resize(int cols, int rows);
}

/// <summary>Credentials for one connect attempt. Held in memory only for the attempt.</summary>
public sealed record SshCredentials(string? Password, string? KeyPassphrase)
{
    public override string ToString() => "SshCredentials { *** }";
}

/// <summary>The server rejected the credentials (as opposed to a network or host-key failure), so a
/// re-prompt makes sense.</summary>
public sealed class AuthenticationFailedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The user declined the host key, or it changed and was refused. Never retried automatically.</summary>
public sealed class HostKeyRejectedException(string message) : Exception(message);
