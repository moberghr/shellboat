namespace Shellboat.Core.Trust;

/// <summary>What is being trusted: an SSH host key or an RDP server certificate.</summary>
public enum TrustKind
{
    SshHostKey,
    RdpCertificate,
}

/// <summary>The outcome of comparing a presented fingerprint against what was trusted before.</summary>
public enum TrustVerdict
{
    /// <summary>Seen before with the same fingerprint.</summary>
    Known,

    /// <summary>Never seen: ask the user.</summary>
    Unknown,

    /// <summary>Seen before with a <b>different</b> fingerprint: possible man-in-the-middle.</summary>
    Changed,
}

/// <summary>Remembers fingerprints the user accepted (Shellboat's own known_hosts, for both protocols).</summary>
public interface ITrustStore
{
    Task<TrustVerdict> CheckAsync(TrustKind kind, string host, int port, string fingerprint, CancellationToken ct);

    Task TrustAsync(TrustKind kind, string host, int port, string fingerprint, CancellationToken ct);
}

/// <summary>A fingerprint shown to the user for a decision. <see cref="Verdict"/> says whether it is new or changed.</summary>
public sealed record TrustPrompt(TrustKind Kind, string Host, int Port, string Fingerprint, TrustVerdict Verdict, string? Detail = null);

/// <summary>Asks the user whether to trust a host. Implemented by the UI host.</summary>
public interface ITrustPrompter
{
    /// <returns>True when the user accepts (and wants it remembered); false to abort the connection.</returns>
    Task<bool> ConfirmAsync(TrustPrompt prompt, CancellationToken ct);
}
