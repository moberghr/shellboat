namespace Shellboat.Core.Connections;

/// <summary>The protocol a saved connection speaks. Persisted by name, so never renumber or rename.</summary>
public enum ConnectionKind
{
    Ssh,
    Rdp,
}
