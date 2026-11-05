using Shellboat.Core.Connections;

namespace Shellboat.App.Connections;

/// <summary>
/// Pure operations on the connection tree document. Every edit returns a new document; nothing here does
/// I/O, so the tree's rules (no folder inside its own subtree, deleting a folder takes its contents) are
/// unit-tested without a UI.
/// </summary>
public static class ConnectionTreeEditor
{
    public static ConnectionTreeDocument AddFolder(ConnectionTreeDocument doc, Folder folder)
    {
        if (folder.ParentId is { } parent && doc.Folders.All(f => f.Id != parent))
        {
            throw new InvalidOperationException("The parent folder does not exist.");
        }

        return doc with { Folders = [.. doc.Folders, folder with { SortOrder = NextOrder(doc, folder.ParentId) }] };
    }

    public static ConnectionTreeDocument RenameFolder(ConnectionTreeDocument doc, Guid folderId, string name)
        => doc with { Folders = [.. doc.Folders.Select(f => f.Id == folderId ? f with { Name = name } : f)] };

    /// <summary>Insert or replace a connection (matched by id).</summary>
    /// <summary>Sets (or with null, resets) the icon of the folder or connection <paramref name="id"/>.</summary>
    public static ConnectionTreeDocument SetIcon(ConnectionTreeDocument doc, Guid id, NodeIcon? icon) => doc with
    {
        Folders = [.. doc.Folders.Select(f => f.Id == id ? f with { Icon = icon } : f)],
        Connections = [.. doc.Connections.Select(c => c.Id == id ? c with { Icon = icon } : c)],
    };

    public static ConnectionTreeDocument Upsert(ConnectionTreeDocument doc, ConnectionEntry entry)
    {
        if (entry.FolderId is { } folder && doc.Folders.All(f => f.Id != folder))
        {
            throw new InvalidOperationException("The target folder does not exist.");
        }

        var existing = doc.Connections.FirstOrDefault(c => c.Id == entry.Id);
        if (existing is null)
        {
            return doc with { Connections = [.. doc.Connections, entry with { SortOrder = NextOrder(doc, entry.FolderId) }] };
        }

        return doc with { Connections = [.. doc.Connections.Select(c => c.Id == entry.Id ? entry : c)] };
    }

    public static ConnectionTreeDocument DeleteConnection(ConnectionTreeDocument doc, Guid connectionId)
        => doc with { Connections = [.. doc.Connections.Where(c => c.Id != connectionId)] };

    /// <summary>Delete a folder and everything under it. Returns the removed connection ids so the caller
    /// can delete their secrets too.</summary>
    public static (ConnectionTreeDocument Document, IReadOnlyList<Guid> RemovedConnections) DeleteFolder(ConnectionTreeDocument doc, Guid folderId)
    {
        var doomed = Subtree(doc, folderId);
        var removed = doc.Connections.Where(c => c.FolderId is { } f && doomed.Contains(f)).Select(c => c.Id).ToList();
        var next = doc with
        {
            Folders = [.. doc.Folders.Where(f => !doomed.Contains(f.Id))],
            Connections = [.. doc.Connections.Where(c => !(c.FolderId is { } f && doomed.Contains(f)))],
        };
        return (next, removed);
    }

    public static ConnectionTreeDocument MoveConnection(ConnectionTreeDocument doc, Guid connectionId, Guid? targetFolderId)
    {
        if (targetFolderId is { } target && doc.Folders.All(f => f.Id != target))
        {
            throw new InvalidOperationException("The target folder does not exist.");
        }

        var order = NextOrder(doc, targetFolderId);
        return doc with
        {
            Connections = [.. doc.Connections.Select(c => c.Id == connectionId ? c with { FolderId = targetFolderId, SortOrder = order } : c)],
        };
    }

    /// <summary>Move a folder under another (null = top level). Refuses to move a folder into its own subtree.</summary>
    public static ConnectionTreeDocument MoveFolder(ConnectionTreeDocument doc, Guid folderId, Guid? targetParentId)
    {
        if (!CanMoveFolder(doc, folderId, targetParentId))
        {
            throw new InvalidOperationException("A folder cannot be moved into itself or one of its subfolders.");
        }

        var order = NextOrder(doc, targetParentId);
        return doc with
        {
            Folders = [.. doc.Folders.Select(f => f.Id == folderId ? f with { ParentId = targetParentId, SortOrder = order } : f)],
        };
    }

    public static bool CanMoveFolder(ConnectionTreeDocument doc, Guid folderId, Guid? targetParentId)
        => targetParentId is not { } target || !Subtree(doc, folderId).Contains(target);

    /// <summary>The folder and all its descendants.</summary>
    public static HashSet<Guid> Subtree(ConnectionTreeDocument doc, Guid folderId)
    {
        var result = new HashSet<Guid> { folderId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var f in doc.Folders)
            {
                if (f.ParentId is { } p && result.Contains(p) && result.Add(f.Id))
                {
                    changed = true;
                }
            }
        }

        return result;
    }

    /// <summary>Find or create a folder path (e.g. KeePass groups) under <paramref name="parentId"/>.</summary>
    public static (ConnectionTreeDocument Document, Guid? FolderId) EnsureFolderPath(ConnectionTreeDocument doc, Guid? parentId, IEnumerable<string> path)
    {
        var current = parentId;
        foreach (var name in path)
        {
            var existing = doc.Folders.FirstOrDefault(f => f.ParentId == current && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new Folder(Guid.NewGuid(), name, current);
                doc = AddFolder(doc, existing);
            }

            current = existing.Id;
        }

        return (doc, current);
    }

    /// <summary>
    /// Merge an imported tree under <paramref name="rootFolderId"/>: its top-level items are re-parented there,
    /// and folders/connections whose ids already exist are updated in place (so re-importing the same source
    /// refreshes instead of duplicating). Returns how many connections were new vs updated.
    /// </summary>
    public static (ConnectionTreeDocument Document, int Added, int Updated) Merge(ConnectionTreeDocument doc, ConnectionTreeDocument imported, Guid? rootFolderId)
    {
        var folders = doc.Folders.ToDictionary(f => f.Id);
        foreach (var f in imported.Folders)
        {
            // An importer never sets icons; keep the one the user chose.
            var icon = f.Icon ?? (folders.TryGetValue(f.Id, out var existing) ? existing.Icon : null);
            folders[f.Id] = f with { ParentId = f.ParentId ?? rootFolderId, Icon = icon };
        }

        var connections = doc.Connections.ToDictionary(c => c.Id);
        int added = 0, updated = 0;
        foreach (var c in imported.Connections)
        {
            var icon = c.Icon;
            if (connections.TryGetValue(c.Id, out var existing))
            {
                updated++;
                icon ??= existing.Icon;
            }
            else
            {
                added++;
            }

            connections[c.Id] = c with { FolderId = c.FolderId ?? rootFolderId, Icon = icon };
        }

        // Keep the original order of existing items, new ones appended.
        var orderedFolders = doc.Folders.Select(f => folders[f.Id]).Concat(imported.Folders.Where(f => doc.Folders.All(d => d.Id != f.Id)).Select(f => folders[f.Id])).ToList();
        var orderedConnections = doc.Connections.Select(c => connections[c.Id]).Concat(imported.Connections.Where(c => doc.Connections.All(d => d.Id != c.Id)).Select(c => connections[c.Id])).ToList();
        return (new ConnectionTreeDocument(orderedFolders, orderedConnections), added, updated);
    }

    /// <summary>Case-insensitive match on name, host or user — what the tree's filter box searches.</summary>
    public static bool Matches(ConnectionEntry entry, string filter)
        => string.IsNullOrWhiteSpace(filter)
        || entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || entry.Host.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || entry.UserName.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static int NextOrder(ConnectionTreeDocument doc, Guid? parent)
    {
        var folders = doc.Folders.Where(f => f.ParentId == parent).Select(f => f.SortOrder);
        var connections = doc.Connections.Where(c => c.FolderId == parent).Select(c => c.SortOrder);
        return folders.Concat(connections).DefaultIfEmpty(-1).Max() + 1;
    }
}
