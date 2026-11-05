using System.Net.Sockets;
using Renci.SshNet;
using Renci.SshNet.Common;
using Shellboat.Core.Connections;
using Shellboat.Core.Sessions;
using Shellboat.Core.Trust;

namespace Shellboat.Ssh;

/// <summary>
/// What every SSH.NET client in Shellboat shares: connection info + auth methods from a <see cref="ConnectionEntry"/>,
/// host-key verification against our trust store (unknown → prompt, changed → hard block), and mapping SSH.NET's
/// failures to <see cref="AuthenticationFailedException"/> / <see cref="HostKeyRejectedException"/> / <see cref="IOException"/>.
/// </summary>
internal sealed class SshLogin
{
    private readonly ConnectionEntry _connection;
    private readonly SshCredentials _credentials;
    private readonly ITrustStore _trust;
    private readonly ITrustPrompter _prompter;
    private string? _hostKeyFailure;

    public SshLogin(ConnectionEntry connection, SshCredentials credentials, ITrustStore trust, ITrustPrompter prompter)
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

    /// <summary>Builds the client with <paramref name="create"/>, hooks host-key checks and connects it.</summary>
    public async Task<T> ConnectAsync<T>(Func<ConnectionInfo, T> create, CancellationToken ct)
        where T : BaseClient
    {
        T? client = null;
        try
        {
            // Inside the try: loading the key file is where a missing/wrong passphrase first surfaces.
            var info = new ConnectionInfo(_connection.Host, _connection.EffectivePort, _connection.UserName, BuildAuthMethods())
            {
                Timeout = TimeSpan.FromSeconds(20),
            };

            client = create(info);
            client.HostKeyReceived += (_, e) => e.CanTrust = VerifyHostKey(e, ct);
            await client.ConnectAsync(ct).ConfigureAwait(false);
            return client;
        }
        catch (Exception ex) when (Map(ex) is { } mapped)
        {
            client?.Dispose();
            throw mapped;
        }
        catch
        {
            client?.Dispose();
            throw;
        }
    }

    private Exception? Map(Exception ex) => ex switch
    {
        SshConnectionException when _hostKeyFailure is not null => new HostKeyRejectedException(_hostKeyFailure),
        SshAuthenticationException => new AuthenticationFailedException($"Authentication failed for {_connection.UserName}@{_connection.Host}: {ex.Message}", ex),
        SshPassPhraseNullOrEmptyException => new AuthenticationFailedException("The private key is encrypted and needs a passphrase.", ex),
        InvalidOperationException or SshException when ex.Message.Contains("passphrase", StringComparison.OrdinalIgnoreCase)
            => new AuthenticationFailedException($"The private key could not be decrypted: {ex.Message}", ex),
        SocketException => new IOException($"Could not reach {_connection.Host}:{_connection.EffectivePort}: {ex.Message}", ex),
        _ => null,
    };

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

    internal static string ExpandHome(string path)
    {
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Length > 2 ? path[2..] : "");
        }

        return path;
    }
}
