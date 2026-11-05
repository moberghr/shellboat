namespace Shellboat.Core.Connections;

/// <summary>
/// One saved connection. Exactly one of <see cref="Ssh"/> / <see cref="Rdp"/> is set, matching
/// <see cref="Kind"/>. Secrets are never part of this record — they live in <see cref="Secrets.ISecretStore"/>
/// keyed by <see cref="Id"/>.
/// </summary>
public sealed record ConnectionEntry
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid? FolderId { get; init; }
    public int SortOrder { get; init; }
    public required ConnectionKind Kind { get; init; }
    public required string Host { get; init; }
    public int Port { get; init; }
    public string UserName { get; init; } = "";
    public string Notes { get; init; } = "";
    public SshSettings? Ssh { get; init; }
    public RdpSettings? Rdp { get; init; }

    public int EffectivePort => Port > 0 ? Port : DefaultPort(Kind);

    public static int DefaultPort(ConnectionKind kind) => kind switch
    {
        ConnectionKind.Ssh => 22,
        ConnectionKind.Rdp => 3389,
        _ => 0,
    };
}

public enum SshAuthMethod
{
    Password,
    PrivateKey,
}

/// <param name="PrivateKeyPath">Path to an OpenSSH/PEM private key; used when <paramref name="Auth"/> is
/// <see cref="SshAuthMethod.PrivateKey"/>. Its passphrase (if any) is a secret, not stored here.</param>
public sealed record SshSettings(SshAuthMethod Auth = SshAuthMethod.Password, string? PrivateKeyPath = null);

public enum RdpResolutionMode
{
    /// <summary>Follow the tab's size, re-negotiating on resize (display control channel).</summary>
    Dynamic,
    Fixed,
}

public sealed record RdpSettings(
    string Domain = "",
    RdpResolutionMode Resolution = RdpResolutionMode.Dynamic,
    int Width = 1920,
    int Height = 1080,
    int ColorDepth = 32);
