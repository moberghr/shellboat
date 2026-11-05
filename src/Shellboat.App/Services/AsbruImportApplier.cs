using Shellboat.App.Connections;
using Shellboat.App.ViewModels;
using Shellboat.Core.Secrets;
using Shellboat.Persistence.Import;

namespace Shellboat.App.Services;

/// <summary>What applying an Ásbrú import did, as lines for the summary dialog (first line = status bar).</summary>
public sealed record AsbruImportOutcome(int Added, int Updated, int SecretsSaved, int SecretsDropped, IReadOnlyList<string> Notes);

/// <summary>
/// Puts an <see cref="AsbruImportResult"/> into the tree under "Imported from Ásbrú" (merged by Ásbrú's UUIDs, so a
/// re-import updates) and its plain-text passwords into the keychain. With no keychain they are dropped and the
/// summary says so — never stored anywhere else (§1.1).
/// </summary>
public static class AsbruImportApplier
{
    public const string RootFolderName = "Imported from Ásbrú";

    public static async Task<AsbruImportOutcome> ApplyAsync(ConnectionTreeViewModel tree, ISecretStore secrets, AsbruImportResult result, CancellationToken ct)
    {
        var (doc, root) = ConnectionTreeEditor.EnsureFolderPath(tree.Document, null, [RootFolderName]);
        var (merged, added, updated) = ConnectionTreeEditor.Merge(doc, result.Tree, root);
        await tree.ApplyAsync(merged, root);

        int saved = 0, dropped = 0;
        foreach (var secret in result.Secrets)
        {
            if (!secrets.CanStore)
            {
                dropped++;
                continue;
            }

            try
            {
                await secrets.SetAsync(secret.ConnectionId, secret.Purpose, secret.Value, ct);
                saved++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                dropped++;
            }
        }

        var notes = new List<string> { $"{added} new, {updated} updated connection(s); {saved} password(s) saved to the keychain." };
        if (dropped > 0)
        {
            notes.Add(secrets.CanStore
                ? $"{dropped} password(s) could not be saved to the keychain — you'll be asked on connect."
                : $"{dropped} password(s) were not kept: there is no system keychain ({secrets.UnavailableReason}). You'll be asked on connect.");
        }

        if (result.EncryptedPasswordsNotImported > 0)
        {
            notes.Add($"{result.EncryptedPasswordsNotImported} password(s) are stored encrypted by Ásbrú and were not imported — export from Ásbrú instead of using asbru.yml, or enter them on first connect.");
        }

        if (result.SkippedUnsupported > 0)
        {
            notes.Add($"{result.SkippedUnsupported} connection(s) of a type Shellboat doesn't support yet (VNC, telnet, SFTP, generic commands…) were skipped.");
        }

        if (result.JumpHostsIgnored > 0)
        {
            notes.Add($"{result.JumpHostsIgnored} connection(s) use a jump host, which Shellboat doesn't support yet; they were imported without it.");
        }

        if (result.CertificateChecksDropped > 0)
        {
            notes.Add($"{result.CertificateChecksDropped} RDP connection(s) had certificate checks turned off (/cert-ignore). Shellboat always checks: you'll confirm each server's certificate once.");
        }

        if (result.Secrets.Count > 0)
        {
            notes.Add("The export file holds these passwords in plain text — delete it now that they're imported.");
        }

        return new AsbruImportOutcome(added, updated, saved, dropped, notes);
    }
}
