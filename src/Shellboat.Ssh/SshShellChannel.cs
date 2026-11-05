using Renci.SshNet;
using Renci.SshNet.Common;
using Shellboat.Core.Connections;
using Shellboat.Core.Sessions;
using Shellboat.Core.Trust;

namespace Shellboat.Ssh;

/// <summary>
/// One interactive SSH shell (SSH.NET): connect, verify the host key against Shellboat's trust store,
/// authenticate by password (incl. keyboard-interactive) or private key, open an xterm-256color PTY.
/// </summary>
public sealed class SshShellChannel : IShellChannel
{
    private const string TermName = "xterm-256color";
    private readonly SshLogin _login;
    private SshClient? _client;
    private ShellStream? _shell;
    private int _closed;

    public SshShellChannel(ConnectionEntry connection, SshCredentials credentials, ITrustStore trust, ITrustPrompter prompter)
    {
        _login = new SshLogin(connection, credentials, trust, prompter);
    }

    public event Action<byte[]>? DataReceived;

    public event Action<string?>? Closed;

    public async Task ConnectAsync(int cols, int rows, CancellationToken ct)
    {
        var client = await _login.ConnectAsync(info => new SshClient(info), ct).ConfigureAwait(false);
        client.ErrorOccurred += (_, e) => RaiseClosed(e.Exception.Message);
        _client = client;

        var modes = new Dictionary<TerminalModes, uint> { [TerminalModes.ECHO] = 1 };
        var shell = client.CreateShellStream(TermName, (uint)cols, (uint)rows, 0, 0, 64 * 1024, modes);
        shell.DataReceived += (_, e) => DataReceived?.Invoke(e.Data);
        shell.Closed += (_, _) => RaiseClosed(null);
        shell.ErrorOccurred += (_, e) => RaiseClosed(e.Exception.Message);
        _shell = shell;
    }

    public void Write(byte[] data)
    {
        var shell = _shell;
        if (shell is null || Volatile.Read(ref _closed) != 0)
        {
            return;
        }

        try
        {
            shell.Write(data, 0, data.Length);
            shell.Flush();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SshException or IOException)
        {
            RaiseClosed(ex.Message);
        }
    }

    public void Resize(int cols, int rows)
    {
        if (cols <= 0 || rows <= 0 || Volatile.Read(ref _closed) != 0)
        {
            return;
        }

        try
        {
            _shell?.ChangeWindowSize((uint)cols, (uint)rows, 0, 0);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SshException)
        {
            // The channel is going away; Closed reports it.
        }
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _closed, 1);
        try
        {
            _shell?.Dispose();
        }
        catch (Exception)
        {
            // Best-effort teardown.
        }

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

    private void RaiseClosed(string? reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            Closed?.Invoke(reason);
        }
    }
}
