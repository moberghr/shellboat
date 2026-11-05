using Shellboat.Core.Secrets;

namespace Shellboat.Persistence.Secrets;

/// <summary>
/// Picks this platform's OS credential store when it actually works (decided by a store → read back →
/// delete probe), else <see cref="NoSecretStore"/> carrying the reason. No on-disk fallback.
/// </summary>
public static class SecretStoreFactory
{
    private const string ProbeValue = "probe";

    public static async Task<ISecretStore> CreateAsync(CancellationToken ct = default)
    {
        var store = PlatformStore();
        if (store is null)
        {
            return new NoSecretStore("no OS credential store is implemented for this platform.");
        }

        var failure = await ProbeFailureAsync(store, ct).ConfigureAwait(false);
        return failure is null ? store : new NoSecretStore(failure);
    }

    private static ISecretStore? PlatformStore()
    {
        if (OperatingSystem.IsLinux())
        {
            return new SecretToolSecretStore();
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialSecretStore();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacKeychainSecretStore();
        }

        return null;
    }

    /// <returns>Null when the store works; otherwise a short reason it was rejected (no secrets in it).</returns>
    internal static async Task<string?> ProbeFailureAsync(ISecretStore store, CancellationToken ct)
    {
        var probe = Guid.NewGuid();
        try
        {
            await store.SetAsync(probe, SecretPurpose.Password, ProbeValue, ct).ConfigureAwait(false);
            var readBack = await store.GetAsync(probe, SecretPurpose.Password, ct).ConfigureAwait(false);
            return readBack == ProbeValue ? null
                 : readBack is null ? "it accepted the probe secret and then read it back as missing."
                 : "it read the probe secret back as a different value.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            try
            {
                await store.DeleteAsync(probe, SecretPurpose.Password, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best effort: a probe that could not be cleaned up is not worth failing startup over.
            }
        }
    }
}
