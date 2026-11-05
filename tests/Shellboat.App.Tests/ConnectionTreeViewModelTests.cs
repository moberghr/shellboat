using Shellboat.App.ViewModels;
using Shellboat.Core.Connections;
using Shellboat.Core.Secrets;

namespace Shellboat.App.Tests;

public class ConnectionTreeViewModelTests
{
    [Fact]
    public async Task Edits_persist_and_deleting_a_folder_forgets_its_secrets()
    {
        var store = new MemoryConnectionStore();
        var secrets = new FakeSecrets();
        var tree = new ConnectionTreeViewModel(store, secrets);
        await tree.LoadAsync(default);

        var folder = await tree.AddFolderAsync("Prod", null);
        var entry = new ConnectionEntry { Id = Guid.NewGuid(), Name = "web", FolderId = folder.Id, Kind = ConnectionKind.Ssh, Host = "h" };
        await tree.UpsertAsync(entry);
        secrets.Items[(entry.Id, SecretPurpose.Password)] = "pw";

        var root = Assert.Single(tree.Roots);
        Assert.Equal("Prod", root.Title);
        Assert.Equal("web", Assert.Single(root.Children).Title);
        Assert.Equal(entry.Id, tree.Selected?.Id);

        await tree.DeleteFolderAsync(folder.Id);
        Assert.Empty(tree.Roots);
        Assert.Empty(store.Saved.Connections);
        Assert.Empty(secrets.Items);
    }

    [Fact]
    public async Task Filter_shows_only_matches_and_their_folders()
    {
        var store = new MemoryConnectionStore();
        var tree = new ConnectionTreeViewModel(store, new FakeSecrets());
        var a = await tree.AddFolderAsync("A", null);
        await tree.AddFolderAsync("Empty", null);
        await tree.UpsertAsync(new ConnectionEntry { Id = Guid.NewGuid(), Name = "db1", FolderId = a.Id, Kind = ConnectionKind.Ssh, Host = "db1" });
        await tree.UpsertAsync(new ConnectionEntry { Id = Guid.NewGuid(), Name = "web", Kind = ConnectionKind.Ssh, Host = "web" });

        tree.Filter = "db";

        var only = Assert.Single(tree.Roots);
        Assert.Equal("A", only.Title);
        Assert.True(only.IsExpanded);
        Assert.Equal("db1", Assert.Single(only.Children).Title);
    }

    [Fact]
    public async Task Import_is_offered_only_while_no_connection_exists()
    {
        var tree = new ConnectionTreeViewModel(new MemoryConnectionStore(), new FakeSecrets());
        var changes = new List<string?>();
        tree.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        await tree.LoadAsync(default);
        Assert.True(tree.HasNoConnections);

        var folder = await tree.AddFolderAsync("Only a folder", null);
        Assert.True(tree.HasNoConnections);

        await tree.UpsertAsync(new ConnectionEntry { Id = Guid.NewGuid(), Name = "web", FolderId = folder.Id, Kind = ConnectionKind.Ssh, Host = "h" });
        Assert.False(tree.HasNoConnections);
        Assert.Contains(nameof(ConnectionTreeViewModel.HasNoConnections), changes);

        await tree.DeleteFolderAsync(folder.Id);
        Assert.True(tree.HasNoConnections);
    }
}
