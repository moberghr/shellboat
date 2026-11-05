using System.Net.Sockets;
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
    private readonly ConnectionEntry _connection;
    private readonly SshCredentials _credentials;
    private readonly ITrustStore _trust;
    private readonly ITrustPrompter _prompter;
    private SshClient? _client;
    private ShellStream? _shell;
    private int _closed;
    private string? _hostKeyFailure;

    public SshShellChannel(ConnectionEntry connection, SshCredentials credentials, ITrustStore trust, ITrustPrompter prompter)
    {
        if (connection.Kind != ConnectionKind.Ssh)
        {
            throw new ArgumentException("Not an SSH connection.", nameof(connection));
        }

        _connection = connection;
        _credentials = credentials;
        _trust = trust;
        _prompter = prompter;
    }

    public event Action<byte[]>? DataReceived;

    public event Action<string?>? Closed;

    public async Task ConnectAsync(int cols, int rows, CancellationToken ct)
    {
        SshClient client;
        try
        {
            // Inside the try: loading the key file is where a missing/wrong passphrase first surfaces.
            var info = new ConnectionInfo(_connection.Host, _connection.EffectivePort, _connection.UserName, BuildAuthMethods())
            {
                Timeout = TimeSpan.FromSeconds(20),
            };

            client = new SshClient(info);
            client.HostKeyReceived += (_, e) => e.CanTrust = VerifyHostKey(e, ct);
            client.ErrorOccurred += (_, e) => RaiseClosed(e.Exception.Message);
            _client = client;
            await client.ConnectAsync(ct).ConfigureAwait(false);
        }
        catch (SshConnectionException) when (_hostKeyFailure is not null)
        {
            throw new HostKeyRejectedException(_hostKeyFailure);
        }
        catch (SshAuthenticationException ex)
        {
            throw new AuthenticationFailedException($"Authentication failed for {_connection.UserName}@{_connection.Host}: {ex.Message}", ex);
        }
        catch (SshPassPhraseNullOrEmptyException ex)
        {
            throw new AuthenticationFailedException("The private key is encrypted and needs a passphrase.", ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or SshException && ex.Message.Contains("passphrase", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthenticationFailedException($"The private key could not be decrypted: {ex.Message}", ex);
        }
        catch (SocketException ex)
        {
            throw new IOException($"Could not reach {_connection.Host}:{_connection.EffectivePort}: {ex.Message}", ex);
        }

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

    private AuthenticationMethod[] BuildAuthMethods()
    {
        var user = _connection.UserName;
        var ssh = _connection.Ssh ?? new SshSettings();
        if (ssh.Auth == SshAuthMethod.PrivateKey)
        {
            var path = ExpandHome(ssh.PrivateKeyPath ?? "");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Private key not found: {path}", path);
            }

            var key = string.IsNullOrEmpty(_credentials.KeyPassphrase)
                ? new PrivateKeyFile(path)
                : new PrivateKeyFile(path, _credentials.KeyPassphrase);
            return [new PrivateKeyAuthenticationMethod(user, key)];
        }

        var password = _credentials.Password ?? "";
        var interactive = new KeyboardInteractiveAuthenticationMethod(user);
        interactive.AuthenticationPrompt += (_, e) =>
        {
            // Servers that disable plain "password" auth ask the same question this way.
            foreach (var prompt in e.Prompts)
            {
                if (prompt.Request.Contains("password", StringComparison.OrdinalIgnoreCase))
                {
                    prompt.Response = password;
                }
            }
        };
        return [new PasswordAuthenticationMethod(user, password), interactive];
    }

    /// <summary>Runs on SSH.NET's key-exchange thread; blocking it while the user decides is intended.</summary>
    private bool VerifyHostKey(HostKeyEventArgs e, CancellationToken ct)
    {
        var fingerprint = $"{e.HostKeyName} SHA256:{e.FingerPrintSHA256}";
        var host = _connection.Host;
        var port = _connection.EffectivePort;
        var verdict = _trust.CheckAsync(TrustKind.SshHostKey, host, port, fingerprint, ct).GetAwaiter().GetResult();
        if (verdict == TrustVerdict.Known)
        {
            return true;
        }

        if (verdict == TrustVerdict.Changed)
        {
            // Hard block, like OpenSSH: a changed key is either a reinstalled server or a man in the middle,
            // and a click-through dialog cannot tell the user which. They remove the old entry deliberately.
            _hostKeyFailure = $"WARNING: the host key for {host}:{port} has CHANGED (now {fingerprint}). "
                + "This could be a man-in-the-middle attack. If the server was reinstalled, remove its entry from "
                + "known_hosts.json in Shellboat's config folder and connect again.";
            return false;
        }

        var accepted = _prompter.ConfirmAsync(new TrustPrompt(TrustKind.SshHostKey, host, port, fingerprint, verdict), ct)
            .GetAwaiter().GetResult();
        if (!accepted)
        {
            _hostKeyFailure = $"The host key for {host} was not accepted.";
            return false;
        }

        _trust.TrustAsync(TrustKind.SshHostKey, host, port, fingerprint, ct).GetAwaiter().GetResult();
        return true;
    }

    private void RaiseClosed(string? reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            Closed?.Invoke(reason);
        }
    }

    internal static string ExpandHome(string path)
    {
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Length > 2 ? path[2..] : "");
        }

        return path;
    }
}
