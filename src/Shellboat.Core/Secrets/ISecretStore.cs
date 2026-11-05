namespace Shellboat.Core.Secrets;

/// <summary>Which secret of a connection: its login password, or the passphrase of its private key.</summary>
public enum SecretPurpose
{
    Password,
    KeyPassphrase,
}

/// <summary>
/// Stores connection secrets in the OS credential store, keyed by connection id + purpose. There is no
/// on-disk fallback: a store that cannot keep a secret safely refuses (<see cref="SecretStorageRefusedException"/>)
/// and the app prompts and keeps the secret in memory instead (§1.1).
/// </summary>
public interface ISecretStore
{
    Task SetAsync(Guid connectionId, SecretPurpose purpose, string secret, CancellationToken ct);

    Task<string?> GetAsync(Guid connectionId, SecretPurpose purpose, CancellationToken ct);

    Task DeleteAsync(Guid connectionId, SecretPurpose purpose, CancellationToken ct);

    /// <summary>Whether <see cref="SetAsync"/> will actually store anything.</summary>
    bool CanStore { get; }

    /// <summary>Why nothing can be stored, in the words of whatever refused (redacted); null when it can.</summary>
    string? UnavailableReason => null;
}

/// <summary>A secret was offered to a store that cannot hold it. A policy outcome, not an error.</summary>
public sealed class SecretStorageRefusedException(string message) : Exception(message);
