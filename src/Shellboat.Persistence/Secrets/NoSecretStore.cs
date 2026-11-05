using Shellboat.Core.Secrets;

namespace Shellboat.Persistence.Secrets;

/// <summary>
/// Used when no OS credential store works here: holds nothing, refuses writes. There is deliberately no
/// on-disk fallback — the app prompts and keeps the secret in memory for the session (§1.1).
/// </summary>
public sealed class NoSecretStore(string? reason = null) : ISecretStore
{
    public bool CanStore => false;

    public string? UnavailableReason { get; } = reason;

    public Task SetAsync(Guid connectionId, SecretPurpose purpose, string secret, CancellationToken ct)
        => throw new SecretStorageRefusedException(
            "No system keychain is available, so this secret was not saved. Shellboat will ask for it when you connect.");

    public Task<string?> GetAsync(Guid connectionId, SecretPurpose purpose, CancellationToken ct)
        => Task.FromResult<string?>(null);

    public Task DeleteAsync(Guid connectionId, SecretPurpose purpose, CancellationToken ct) => Task.CompletedTask;
}
