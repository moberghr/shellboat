using Shellboat.Core.Connections;

namespace Shellboat.Persistence.Tests;

public class JsonConnectionStoreTests
{
    [Fact]
    public async Task Missing_file_loads_empty()
    {
        using var dir = new TempDir();
        var doc = await new JsonConnectionStore(dir.File("connections.json")).LoadAsync(default);
        Assert.Empty(doc.Folders);
        Assert.Empty(doc.Connections);
    }

    [Fact]
    public async Task Round_trips_folders_and_both_kinds()
    {
        using var dir = new TempDir();
        var store = new JsonConnectionStore(dir.File("connections.json"));
        var folder = new Folder(Guid.NewGuid(), "Prod", null);
        var ssh = new ConnectionEntry
        {
            Id = Guid.NewGuid(), Name = "web", FolderId = folder.Id, Kind = ConnectionKind.Ssh, Host = "web.example", Port = 2222,
            UserName = "deploy", Ssh = new SshSettings(SshAuthMethod.PrivateKey, "~/.ssh/id_ed25519"),
        };
        var rdp = new ConnectionEntry
        {
            Id = Guid.NewGuid(), Name = "dc", Kind = ConnectionKind.Rdp, Host = "dc.example", UserName = "admin",
            Rdp = new RdpSettings("CORP", RdpResolutionMode.Fixed, 1600, 900, 24),
        };

        await store.SaveAsync(new ConnectionTreeDocument([folder], [ssh, rdp]), default);
        var loaded = await store.LoadAsync(default);

        Assert.Equal(folder, Assert.Single(loaded.Folders));
        Assert.Equal(ssh with { Ssh = ssh.Ssh }, loaded.Connections.Single(c => c.Id == ssh.Id));
        Assert.Equal(rdp.Rdp, loaded.Connections.Single(c => c.Id == rdp.Id).Rdp);
        Assert.Equal(2222, loaded.Connections.Single(c => c.Id == ssh.Id).EffectivePort);
        Assert.Equal(3389, loaded.Connections.Single(c => c.Id == rdp.Id).EffectivePort);
    }

    [Fact]
    public async Task File_contains_no_secret_fields_and_enums_by_name()
    {
        using var dir = new TempDir();
        var path = dir.File("connections.json");
        var entry = new ConnectionEntry { Id = Guid.NewGuid(), Name = "x", Kind = ConnectionKind.Rdp, Host = "h", Rdp = new RdpSettings() };
        await new JsonConnectionStore(path).SaveAsync(new ConnectionTreeDocument([], [entry]), default);

        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"Rdp\"", json);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Corrupt_file_throws_instead_of_loading_empty()
    {
        using var dir = new TempDir();
        var path = dir.File("connections.json");
        await File.WriteAllTextAsync(path, "{ not json");
        await Assert.ThrowsAsync<InvalidDataException>(() => new JsonConnectionStore(path).LoadAsync(default));
    }

    [SkippableFact]
    public async Task Saved_file_is_owner_only_on_unix()
    {
        Skip.If(OperatingSystem.IsWindows(), "Unix file modes only");
        using var dir = new TempDir();
        var path = dir.File("connections.json");
        await new JsonConnectionStore(path).SaveAsync(ConnectionTreeDocument.Empty, default);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
    }
}

public class JsonConnectionStoreIconTests
{
    [Fact]
    public async Task Icons_round_trip_on_folders_and_connections()
    {
        using var dir = new TempDir();
        var store = new JsonConnectionStore(dir.File("connections.json"));
        var folder = new Folder(Guid.NewGuid(), "Prod", null, 0, new NodeIcon("server", null));
        var conn = new ConnectionEntry
        {
            Id = Guid.NewGuid(), Name = "db", Kind = ConnectionKind.Ssh, Host = "db", Icon = new NodeIcon("database", "#E3B457"),
        };

        await store.SaveAsync(new ConnectionTreeDocument([folder], [conn]), default);
        var loaded = await store.LoadAsync(default);

        Assert.Equal(new NodeIcon("server", null), loaded.Folders.Single().Icon);
        Assert.Equal(new NodeIcon("database", "#E3B457"), loaded.Connections.Single().Icon);
        Assert.Contains("\"glyph\": \"database\"", await File.ReadAllTextAsync(dir.File("connections.json")));
    }

    [Fact]
    public async Task Files_from_before_icons_load_with_no_icon()
    {
        using var dir = new TempDir();
        var id = Guid.NewGuid();
        await File.WriteAllTextAsync(dir.File("connections.json"), $$"""
            { "folders": [ { "id": "{{Guid.NewGuid()}}", "name": "Old", "sortOrder": 0 } ],
              "connections": [ { "id": "{{id}}", "name": "web", "kind": "Ssh", "host": "web" } ] }
            """);

        var loaded = await new JsonConnectionStore(dir.File("connections.json")).LoadAsync(default);

        Assert.Null(loaded.Folders.Single().Icon);
        Assert.Null(loaded.Connections.Single().Icon);
    }
}
