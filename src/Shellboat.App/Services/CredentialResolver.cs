using Shellboat.Core.Connections;
using Shellboat.Core.Secrets;

namespace Shellboat.App.Services;

/// <summary>
/// Where a connection's secret comes from at connect time: the OS keychain first, otherwise the user.
/// A prompted secret is saved only when the user ticks "remember" and the keychain can hold it; otherwise
/// it lives in memory for this attempt (§1.1).
/// </summary>
public sealed class CredentialResolver(ISecretStore secrets, IDialogService dialogs)
{
    /// <summary>Saved secret, or null when none is stored (or the keychain read failed — then we prompt).</summary>
    public async Task<string?> GetSavedAsync(ConnectionEntry entry, SecretPurpose purpose, CancellationToken ct)
    {
        try
        {
            return await secrets.GetAsync(entry.Id, purpose, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Ask the user. Returns null when they cancel.</summary>
    public async Task<string?> PromptAsync(ConnectionEntry entry, SecretPurpose purpose, string? reason, CancellationToken ct)
    {
        var what = purpose == SecretPurpose.KeyPassphrase ? "Key passphrase" : "Password";
        var target = string.IsNullOrEmpty(entry.UserName) ? entry.Host : $"{entry.UserName}@{entry.Host}";
        var message = reason is null ? $"{what} for {target}" : $"{reason}\n\n{what} for {target}";
        var result = await dialogs.PromptSecretAsync(entry.Name, message, secrets.CanStore).ConfigureAwait(false);
        if (result is null)
        {
            return null;
        }

        if (result.Remember && secrets.CanStore)
        {
            try
            {
                await secrets.SetAsync(entry.Id, purpose, result.Secret, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await dialogs.ShowMessageAsync("Not saved", $"The secret could not be saved to the keychain: {ex.Message}").ConfigureAwait(false);
            }
        }

        return result.Secret;
    }
}
