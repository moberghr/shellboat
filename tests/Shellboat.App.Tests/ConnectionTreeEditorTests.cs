using Shellboat.App.Connections;
using Shellboat.Core.Connections;

namespace Shellboat.App.Tests;

public class ConnectionTreeEditorTests
{
    private static ConnectionEntry Ssh(string name, Guid? folder = null) => new()
    {
        Id = Guid.NewGuid(), Name = name, FolderId = folder, Kind = ConnectionKind.Ssh, Host = name + ".example", UserName = "u",
    };

    [Fact]
    public void Deleting_a_folder_removes_its_whole_subtree_and_reports_the_connections()
    {
        var a = new Folder(Guid.NewGuid(), "A", null);
        var b = new Folder(Guid.NewGuid(), "B", a.Id);
        var other = new Folder(Guid.NewGuid(), "Other", null);
        var inA = Ssh("x", a.Id);
        var inB = Ssh("y", b.Id);
        var keep = Ssh("z", other.Id);
        var doc = new ConnectionTreeDocument([a, b, other], [inA, inB, keep]);

        var (next, removed) = ConnectionTreeEditor.DeleteFolder(doc, a.Id);

        Assert.Equal([other], next.Folders);
        Assert.Equal([keep], next.Connections);
        Assert.Equal(new[] { inA.Id, inB.Id }.Order(), removed.Order());
    }

    [Fact]
    public void A_folder_cannot_move_into_its_own_subtree()
    {
        var a = new Folder(Guid.NewGuid(), "A", null);
        var b = new Folder(Guid.NewGuid(), "B", a.Id);
        var doc = new ConnectionTreeDocument([a, b], []);

        Assert.False(ConnectionTreeEditor.CanMoveFolder(doc, a.Id, b.Id));
        Assert.False(ConnectionTreeEditor.CanMoveFolder(doc, a.Id, a.Id));
        Assert.True(ConnectionTreeEditor.CanMoveFolder(doc, b.Id, null));
        Assert.Throws<InvalidOperationException>(() => ConnectionTreeEditor.MoveFolder(doc, a.Id, b.Id));
        Assert.Null(ConnectionTreeEditor.MoveFolder(doc, b.Id, null).Folders.Single(f => f.Id == b.Id).ParentId);
    }

    [Fact]
    public void Upsert_inserts_then_replaces_and_rejects_unknown_folders()
    {
        var c = Ssh("web");
        var doc = ConnectionTreeEditor.Upsert(ConnectionTreeDocument.Empty, c);
        doc = ConnectionTreeEditor.Upsert(doc, c with { Host = "new.example" });
        Assert.Equal("new.example", Assert.Single(doc.Connections).Host);
        Assert.Throws<InvalidOperationException>(() => ConnectionTreeEditor.Upsert(doc, Ssh("x", Guid.NewGuid())));
    }

    [Fact]
    public void EnsureFolderPath_reuses_existing_folders_case_insensitively()
    {
        var (doc, first) = ConnectionTreeEditor.EnsureFolderPath(ConnectionTreeDocument.Empty, null, ["Servers", "Prod"]);
        var (again, second) = ConnectionTreeEditor.EnsureFolderPath(doc, null, ["servers", "PROD"]);
        Assert.Equal(first, second);
        Assert.Equal(2, again.Folders.Count);
    }

    [Theory]
    [InlineData("web", true)]
    [InlineData("EXAMPLE", true)]
    [InlineData("u", true)]
    [InlineData("db", false)]
    [InlineData("", true)]
    public void Filter_matches_name_host_or_user(string filter, bool expected)
        => Assert.Equal(expected, ConnectionTreeEditor.Matches(Ssh("web"), filter));
}

public class ConnectionTreeMergeTests
{
    [Fact]
    public void Merge_reparents_top_level_under_the_root_and_reimport_updates_in_place()
    {
        var royalFolder = new Folder(Guid.NewGuid(), "Prod", null);
        var conn = new ConnectionEntry { Id = Guid.NewGuid(), Name = "web", Kind = ConnectionKind.Ssh, Host = "a" };
        var loose = conn with { Id = Guid.NewGuid(), Name = "loose" };
        var imported = new ConnectionTreeDocument([royalFolder], [conn with { FolderId = royalFolder.Id }, loose]);
        var (doc, root) = ConnectionTreeEditor.EnsureFolderPath(ConnectionTreeDocument.Empty, null, ["Imported"]);

        var (first, added, updated) = ConnectionTreeEditor.Merge(doc, imported, root);
        Assert.Equal((2, 0), (added, updated));
        Assert.Equal(root, first.Folders.Single(f => f.Id == royalFolder.Id).ParentId);
        Assert.Equal(root, first.Connections.Single(c => c.Id == loose.Id).FolderId);

        var changed = imported with { Connections = [conn with { FolderId = royalFolder.Id, Host = "b" }] };
        var (second, added2, updated2) = ConnectionTreeEditor.Merge(first, changed, root);
        Assert.Equal((0, 1), (added2, updated2));
        Assert.Equal(2, second.Connections.Count);
        Assert.Equal("b", second.Connections.Single(c => c.Id == conn.Id).Host);
    }
}
