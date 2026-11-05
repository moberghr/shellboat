using Shellboat.Core.Secrets;

namespace Shellboat.Persistence.Secrets;

/// <summary>
/// macOS login keychain via <c>/usr/bin/security</c> (service = app dir name, account = connection key).
/// The CLI rather than Security.framework because an item's ACL binds to the creating binary's signature:
/// items made by the Apple-signed <c>security</c> tool stay readable across our unsigned rebuilds without an
/// "allow access?" dialog each time (same reasoning as Bearing).
/// <para><b>Known trade-off:</b> <c>add-generic-password -w</c> takes the secret as an argument, briefly
/// visible to the same user's process list; its stdin mode reads /dev/tty and would hang. Nothing is logged.</para>
/// </summary>
public sealed class MacKeychainSecretStore(ICliRunner? runner = null, string? service = null) : ISecretStore
{
    private readonly ICliRunner _runner = runner ?? CliRunner.Instance;
    private readonly string _service = service ?? ShellboatPaths.AppDirName;

    public bool CanStore => true;

    public async Task SetAsync(Guid connectionId, SecretPurpose purpose, string secret, CancellationToken ct)
    {
        var account = SecretKeys.Account(connectionId, purpose);
        var r = await _runner.RunAsync("security",
            ["add-generic-password", "-U", "-a", account, "-s", _service, "-l", $"Shellboat {account}", "-w", secret],
            null, ct).ConfigureAwait(false);
        if (r.Exit != 0)
        {
            throw new InvalidOperationException($"security add-generic-password failed: {r.Detail}");
        }
    }

    public async Task<string?> GetAsync(Guid connectionId, SecretPurpose purpose, CancellationToken ct)
    {
        var r = await _runner.RunAsync("security",
            ["find-generic-password", "-a", SecretKeys.Account(connectionId, purpose), "-s", _service, "-w"],
            null, ct).ConfigureAwait(false);
        if (r.Exit != 0)
        {
            return null;
        }

        return r.Stdout.TrimEnd('\n');
    }

    public async Task DeleteAsync(Guid connectionId, SecretPurpose purpose, CancellationToken ct)
    {
        var r = await _runner.RunAsync("security",
            ["delete-generic-password", "-a", SecretKeys.Account(connectionId, purpose), "-s", _service],
            null, ct).ConfigureAwait(false);
        if (r.Exit == 0)
        {
            return;
        }

        // Non-zero is both "no such item" and a real failure: check the postcondition instead.
        if (await GetAsync(connectionId, purpose, ct).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException($"security delete-generic-password failed — the secret is still in the keychain: {r.Detail}");
        }
    }
}
