namespace Shellboat.Core.Connections;

/// <summary>Everything the tree persists: folders and connections, flat, linked by ids.</summary>
public sealed record ConnectionTreeDocument(IReadOnlyList<Folder> Folders, IReadOnlyList<ConnectionEntry> Connections)
{
    public static ConnectionTreeDocument Empty { get; } = new([], []);
}

/// <summary>Loads and saves the connection tree. Never holds secrets.</summary>
public interface IConnectionStore
{
    /// <summary>A missing file loads as <see cref="ConnectionTreeDocument.Empty"/>. A corrupt file throws —
    /// silently starting empty would let the next save destroy the user's tree.</summary>
    Task<ConnectionTreeDocument> LoadAsync(CancellationToken ct);

    Task SaveAsync(ConnectionTreeDocument document, CancellationToken ct);
}
