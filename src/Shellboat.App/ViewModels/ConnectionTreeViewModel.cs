using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Shellboat.App.Connections;
using Shellboat.Core.Connections;
using Shellboat.Core.Secrets;

namespace Shellboat.App.ViewModels;

/// <summary>
/// The left-hand tree: owns the current <see cref="ConnectionTreeDocument"/>, rebuilds the node view models
/// from it (keeping expansion state), and persists every edit. Edit rules live in
/// <see cref="ConnectionTreeEditor"/>.
/// </summary>
public sealed partial class ConnectionTreeViewModel : ObservableObject
{
    private readonly IConnectionStore _store;
    private readonly ISecretStore _secrets;
    private readonly HashSet<Guid> _expanded = [];

    [ObservableProperty]
    private string _filter = "";

    [ObservableProperty]
    private TreeNodeViewModel? _selected;

    public ConnectionTreeViewModel(IConnectionStore store, ISecretStore secrets)
    {
        _store = store;
        _secrets = secrets;
    }

    public ConnectionTreeDocument Document { get; private set; } = ConnectionTreeDocument.Empty;

    public ObservableCollection<TreeNodeViewModel> Roots { get; } = [];

    public async Task LoadAsync(CancellationToken ct)
    {
        Document = await _store.LoadAsync(ct);
        foreach (var f in Document.Folders)
        {
            _expanded.Add(f.Id);
        }

        Rebuild();
    }

    partial void OnFilterChanged(string value) => Rebuild();

    /// <summary>The folder new items go into: the selected folder, or the selected connection's folder.</summary>
    public Guid? TargetFolderId => Selected switch
    {
        FolderNodeViewModel f => f.Folder.Id,
        ConnectionNodeViewModel c => c.Entry.FolderId,
        _ => null,
    };

    public Task ApplyAsync(ConnectionTreeDocument next, Guid? select = null)
    {
        Document = next;
        if (select is { } id)
        {
            ExpandAncestors(id);
        }

        Rebuild(select);
        return _store.SaveAsync(next, CancellationToken.None);
    }

    public async Task<Folder> AddFolderAsync(string name, Guid? parentId)
    {
        var folder = new Folder(Guid.NewGuid(), name, parentId);
        _expanded.Add(folder.Id);
        if (parentId is { } p)
        {
            _expanded.Add(p);
        }

        await ApplyAsync(ConnectionTreeEditor.AddFolder(Document, folder), folder.Id);
        return folder;
    }

    public Task RenameFolderAsync(Guid folderId, string name)
        => ApplyAsync(ConnectionTreeEditor.RenameFolder(Document, folderId, name), folderId);

    public Task UpsertAsync(ConnectionEntry entry)
        => ApplyAsync(ConnectionTreeEditor.Upsert(Document, entry), entry.Id);

    public async Task DeleteConnectionAsync(Guid id)
    {
        await ApplyAsync(ConnectionTreeEditor.DeleteConnection(Document, id));
        await ForgetSecretsAsync([id]);
    }

    public async Task DeleteFolderAsync(Guid folderId)
    {
        var (next, removed) = ConnectionTreeEditor.DeleteFolder(Document, folderId);
        await ApplyAsync(next);
        await ForgetSecretsAsync(removed);
    }

    public Task MoveConnectionAsync(Guid connectionId, Guid? folderId)
    {
        if (folderId is { } f)
        {
            _expanded.Add(f);
        }

        return ApplyAsync(ConnectionTreeEditor.MoveConnection(Document, connectionId, folderId), connectionId);
    }

    public Task MoveFolderAsync(Guid folderId, Guid? parentId)
    {
        if (!ConnectionTreeEditor.CanMoveFolder(Document, folderId, parentId))
        {
            return Task.CompletedTask;
        }

        if (parentId is { } p)
        {
            _expanded.Add(p);
        }

        return ApplyAsync(ConnectionTreeEditor.MoveFolder(Document, folderId, parentId), folderId);
    }

    public bool CanMoveFolder(Guid folderId, Guid? parentId) => ConnectionTreeEditor.CanMoveFolder(Document, folderId, parentId);

    public void MarkDropTarget(TreeNodeViewModel? target)
    {
        foreach (var node in All(Roots))
        {
            node.IsDropTarget = ReferenceEquals(node, target);
        }
    }

    private async Task ForgetSecretsAsync(IEnumerable<Guid> ids)
    {
        foreach (var id in ids)
        {
            foreach (var purpose in Enum.GetValues<SecretPurpose>())
            {
                try
                {
                    await _secrets.DeleteAsync(id, purpose, CancellationToken.None);
                }
                catch (Exception)
                {
                    // The connection is gone either way; a stale keychain item is harmless and visible to the user.
                }
            }
        }
    }

    private void ExpandAncestors(Guid id)
    {
        var parent = Document.Connections.FirstOrDefault(c => c.Id == id)?.FolderId
            ?? Document.Folders.FirstOrDefault(f => f.Id == id)?.ParentId;
        while (parent is { } p)
        {
            _expanded.Add(p);
            parent = Document.Folders.FirstOrDefault(f => f.Id == p)?.ParentId;
        }
    }

    private void Rebuild(Guid? select = null)
    {
        foreach (var node in All(Roots))
        {
            if (node is FolderNodeViewModel)
            {
                if (node.IsExpanded)
                {
                    _expanded.Add(node.Id);
                }
                else
                {
                    _expanded.Remove(node.Id);
                }
            }
        }

        var keepSelected = select ?? Selected?.Id;
        var filtering = !string.IsNullOrWhiteSpace(Filter);
        Roots.Clear();
        foreach (var node in BuildLevel(null, filtering))
        {
            Roots.Add(node);
        }

        Selected = keepSelected is { } k ? All(Roots).FirstOrDefault(n => n.Id == k) : null;
    }

    private List<TreeNodeViewModel> BuildLevel(Guid? parent, bool filtering)
    {
        var result = new List<(int Order, TreeNodeViewModel Node)>();
        foreach (var folder in Document.Folders.Where(f => f.ParentId == parent))
        {
            var node = new FolderNodeViewModel(folder);
            foreach (var child in BuildLevel(folder.Id, filtering))
            {
                node.Children.Add(child);
            }

            if (filtering && node.Children.Count == 0 && !folder.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            node.IsExpanded = filtering || _expanded.Contains(folder.Id);
            result.Add((folder.SortOrder, node));
        }

        foreach (var entry in Document.Connections.Where(c => c.FolderId == parent))
        {
            if (filtering && !ConnectionTreeEditor.Matches(entry, Filter))
            {
                continue;
            }

            result.Add((entry.SortOrder, new ConnectionNodeViewModel(entry)));
        }

        // Folders first, then by name — the order people expect from a file tree.
        return [.. result
            .OrderBy(r => r.Node is FolderNodeViewModel ? 0 : 1)
            .ThenBy(r => r.Node.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => r.Node)];
    }

    private static IEnumerable<TreeNodeViewModel> All(IEnumerable<TreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in All(node.Children))
            {
                yield return child;
            }
        }
    }
}
