using Shellboat.Core.Secrets;

namespace Shellboat.Persistence.Secrets;

/// <summary>The account/target string a secret is filed under: <c>&lt;guid&gt;</c> for a password,
/// <c>&lt;guid&gt;:passphrase</c> for a key passphrase. Invariant formatting, stable across locales.</summary>
internal static class SecretKeys
{
    public static string Account(Guid id, SecretPurpose purpose) => purpose switch
    {
        SecretPurpose.Password => id.ToString(),
        SecretPurpose.KeyPassphrase => $"{id}:passphrase",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
    };
}
