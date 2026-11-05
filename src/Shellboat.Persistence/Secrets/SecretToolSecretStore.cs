using Shellboat.Core.Secrets;

namespace Shellboat.Persistence.Secrets;

/// <summary>
/// freedesktop Secret Service via libsecret's <c>secret-tool</c>. Keyed by attributes {app, connection};
/// the secret travels on stdin, never as an argument. Writes verify by reading back, and transient
/// transfer faults are retried — both measured necessities on healthy libsecret (see Bearing's SecretRetry).
/// </summary>
public sealed class SecretToolSecretStore(ICliRunner? runner = null, string? app = null) : ISecretStore
{
    private const int Attempts = 3;
    private readonly ICliRunner _runner = runner ?? CliRunner.Instance;
    private readonly string _app = app ?? ShellboatPaths.AppDirName;

    public bool CanStore => true;

    public async Task SetAsync(Guid connectionId, SecretPurpose purpose, string secret, CancellationToken ct)
    {
        var account = SecretKeys.Account(connectionId, purpose);
        string? detail = null;
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var r = await _runner.RunAsync("secret-tool",
                ["store", "--label", $"Shellboat {account}", "app", _app, "connection", account], secret, ct).ConfigureAwait(false);
            if (r.Exit != 0)
            {
                detail = r.Detail;
                if (LooksFinal(r.Stderr))
                {
                    break;
                }

                continue;
            }

            string? readBack;
            try
            {
                readBack = await GetAsync(connectionId, purpose, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                detail = $"the write could not be confirmed: {ex.Message}";
                continue;
            }

            if (readBack == secret)
            {
                return;
            }

            detail = "it accepted the secret and then read it back as missing.";
        }

        throw new InvalidOperationException($"secret-tool store failed: {detail ?? "unknown error"}");
    }

    public async Task<string?> GetAsync(Guid connectionId, SecretPurpose purpose, CancellationToken ct)
    {
        CliResult r = default;
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            r = await _runner.RunAsync("secret-tool",
                ["lookup", "app", _app, "connection", SecretKeys.Account(connectionId, purpose)], null, ct).ConfigureAwait(false);
            if (r.Exit == 0)
            {
                return r.Stdout.TrimEnd('\n');
            }

            // Empty stderr = "nothing stored" — a real answer, not a fault.
            if (r.Stderr.Trim().Length == 0)
            {
                return null;
            }

            if (LooksFinal(r.Stderr))
            {
                break;
            }
        }

        throw new InvalidOperationException($"secret-tool lookup failed: {r.Detail}");
    }

    public async Task DeleteAsync(Guid connectionId, SecretPurpose purpose, CancellationToken ct)
    {
        var r = await _runner.RunAsync("secret-tool",
            ["clear", "app", _app, "connection", SecretKeys.Account(connectionId, purpose)], null, ct).ConfigureAwait(false);
        if (r.Exit == 0)
        {
            return;
        }

        if (await GetAsync(connectionId, purpose, ct).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException($"secret-tool clear failed — the secret is still in the keyring: {r.Detail}");
        }
    }

    /// <summary>The user dismissed an unlock prompt: retrying would just ask again.</summary>
    private static bool LooksFinal(string? detail)
        => detail is not null
        && (detail.Contains("dismissed", StringComparison.OrdinalIgnoreCase)
         || detail.Contains("cancel", StringComparison.OrdinalIgnoreCase));
}
