using Shellboat.App.Connections;
using Shellboat.App.ViewModels;
using Shellboat.Core.Connections;
using Shellboat.Core.Import;

namespace Shellboat.App.Tests;

public class IconCatalogTests
{
    [Fact]
    public void Glyph_names_are_unique_and_include_the_defaults()
    {
        var names = IconCatalog.Glyphs.Select(g => g.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Contains("terminal", names);
        Assert.Contains("monitor", names);
        Assert.Contains("folder", names);
    }

    [Fact]
    public void Chosen_icon_resolves_and_garbage_falls_back_to_the_default()
    {
        Assert.Equal("Glyph.server", IconCatalog.GlyphKey(new NodeIcon("server", null), "Icon.Ssh"));
        Assert.Equal("Icon.Ssh", IconCatalog.GlyphKey(new NodeIcon("no-such-glyph", null), "Icon.Ssh"));
        Assert.Equal("Icon.Ssh", IconCatalog.GlyphKey(null, "Icon.Ssh"));

        Assert.Equal("#E3B457", IconCatalog.BrushKey(new NodeIcon(null, "#E3B457"), "Kind.Ssh"));
        Assert.Equal("Kind.Ssh", IconCatalog.BrushKey(new NodeIcon(null, "red"), "Kind.Ssh"));
        Assert.Equal("Kind.Ssh", IconCatalog.BrushKey(new NodeIcon(null, "#12345G"), "Kind.Ssh"));
    }

    [Theory]
    [InlineData("terminal", null, null, null)]
    [InlineData("server", null, "server", null)]
    [InlineData("terminal", "#e3b457", null, "#E3B457")]
    [InlineData("bogus", "nope", null, null)]
    public void Normalize_stores_only_what_differs_from_the_default(string glyph, string? color, string? expectGlyph, string? expectColor)
    {
        var icon = IconCatalog.Normalize(glyph, color, "terminal");
        Assert.Equal(expectGlyph is null && expectColor is null ? null : new NodeIcon(expectGlyph, expectColor), icon);
    }
}

public class IconTreeTests
{
    private static ConnectionEntry Ssh(NodeIcon? icon = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "web",
        Kind = ConnectionKind.Ssh,
        Host = "web",
        Ssh = new SshSettings(),
        Icon = icon,
    };

    [Fact]
    public void Rows_show_the_chosen_icon_or_the_kind_default()
    {
        var custom = new ConnectionNodeViewModel(Ssh(new NodeIcon("database", "#D2555A")));
        var plain = new ConnectionNodeViewModel(Ssh());
        var folder = new FolderNodeViewModel(new Folder(Guid.NewGuid(), "Prod", null, 0, new NodeIcon("star", null)));

        Assert.Equal(("Glyph.database", "#D2555A"), (custom.IconKey, custom.IconBrushKey));
        Assert.Equal(("Icon.Ssh", "Kind.Ssh"), (plain.IconKey, plain.IconBrushKey));
        Assert.Equal(("Glyph.star", "Text.Muted"), (folder.IconKey, folder.IconBrushKey));
    }

    [Fact]
    public void SetIcon_changes_folders_and_connections_and_null_resets()
    {
        var folder = new Folder(Guid.NewGuid(), "Prod", null);
        var conn = Ssh();
        var doc = new ConnectionTreeDocument([folder], [conn]);

        doc = ConnectionTreeEditor.SetIcon(doc, folder.Id, new NodeIcon("server", null));
        doc = ConnectionTreeEditor.SetIcon(doc, conn.Id, new NodeIcon(null, "#98BB6C"));
        Assert.Equal("server", doc.Folders.Single().Icon!.Glyph);
        Assert.Equal("#98BB6C", doc.Connections.Single().Icon!.Color);

        doc = ConnectionTreeEditor.SetIcon(doc, conn.Id, null);
        Assert.Null(doc.Connections.Single().Icon);
    }

    [Fact]
    public void Reimport_keeps_icons_the_user_chose()
    {
        var folder = new Folder(Guid.NewGuid(), "Prod", null, 0, new NodeIcon("server", null));
        var conn = Ssh(new NodeIcon("database", "#E3B457"));
        var doc = new ConnectionTreeDocument([folder], [conn]);
        var imported = new ConnectionTreeDocument([folder with { Icon = null, Name = "Prod2" }], [conn with { Icon = null, Host = "web2" }]);

        var (merged, _, updated) = ConnectionTreeEditor.Merge(doc, imported, null);

        Assert.Equal(1, updated);
        Assert.Equal(("web2", new NodeIcon("database", "#E3B457")), (merged.Connections.Single().Host, merged.Connections.Single().Icon));
        Assert.Equal(("Prod2", new NodeIcon("server", null)), (merged.Folders.Single().Name, merged.Folders.Single().Icon));
    }

    [Fact]
    public void Editing_a_connection_keeps_its_icon()
    {
        var conn = Ssh(new NodeIcon("cloud", null));
        var editor = new ConnectionEditorViewModel(conn, null, canRememberSecrets: true, hasSavedSecret: false) { Host = "renamed.example" };

        Assert.Equal(new NodeIcon("cloud", null), editor.TryBuild()!.Icon);
    }
}

public class IconPickerViewModelTests
{
    [Fact]
    public void Starts_on_the_current_icon_and_colour()
    {
        var picker = new IconPickerViewModel("web", "terminal", "Kind.Ssh", new NodeIcon("server", "#E3B457"));

        Assert.Equal("server", picker.SelectedGlyph!.Name);
        Assert.Equal("#E3B457", picker.SelectedColor!.Hex);
        Assert.Equal(("Glyph.server", "#E3B457"), (picker.PreviewIconKey, picker.PreviewBrushKey));
    }

    [Fact]
    public void Defaults_mean_no_icon_is_stored()
    {
        var picker = new IconPickerViewModel("web", "terminal", "Kind.Ssh", null);

        Assert.Equal("terminal", picker.SelectedGlyph!.Name);
        Assert.Null(picker.SelectedColor!.Hex);
        Assert.Equal("Kind.Ssh", picker.PreviewBrushKey);
        Assert.Null(picker.Result);
    }

    [Fact]
    public void Choosing_then_resetting_returns_to_default()
    {
        var picker = new IconPickerViewModel("web", "terminal", "Kind.Ssh", null);
        picker.SelectedGlyph = picker.Glyphs.Single(g => g.Name == "bolt");
        picker.SelectedColor = picker.Colors.Single(c => c.Label == "Red");
        Assert.Equal(new NodeIcon("bolt", "#D2555A"), picker.Result);

        picker.ResetCommand.Execute(null);

        Assert.Null(picker.Result);
    }

    [Fact]
    public void A_stored_colour_outside_the_palette_is_kept_selectable()
    {
        var picker = new IconPickerViewModel("web", "terminal", "Kind.Ssh", new NodeIcon(null, "#123456"));

        Assert.Equal("#123456", picker.SelectedColor!.Hex);
        Assert.Equal(new NodeIcon(null, "#123456"), picker.Result);
    }
}

public class ChangeIconCommandTests
{
    private sealed class NoImporter : ICredentialImporter
    {
        public Task<IReadOnlyList<ImportedEntry>> ReadAsync(string databasePath, string masterPassword, string? keyFilePath, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private static async Task<(MainWindowViewModel Vm, MemoryConnectionStore Store, FakeDialogs Dialogs, ConnectionEntry Conn)> CreateAsync()
    {
        var store = new MemoryConnectionStore();
        var tree = new ConnectionTreeViewModel(store, new FakeSecrets());
        var conn = new ConnectionEntry { Id = Guid.NewGuid(), Name = "dc", Kind = ConnectionKind.Rdp, Host = "dc", Rdp = new RdpSettings() };
        await tree.UpsertAsync(conn);
        var dialogs = new FakeDialogs();
        var vm = new MainWindowViewModel(tree, new FakeSecrets(), dialogs,
            (_, _) => throw new NotSupportedException(), (_, _) => throw new NotSupportedException(), new NoImporter());
        return (vm, store, dialogs, conn);
    }

    [Fact]
    public async Task Saving_the_picker_stores_the_icon_and_the_row_shows_it()
    {
        var (vm, store, dialogs, conn) = await CreateAsync();
        dialogs.IconChoice = p => p.SelectedGlyph = p.Glyphs.Single(g => g.Name == "shield");

        await vm.ChangeIconCommand.ExecuteAsync(vm.Tree.Roots.Single());

        Assert.Equal("monitor", dialogs.LastPicker!.Glyphs.First(g => g.Name == "monitor").Name);
        Assert.Equal("Kind.Rdp", dialogs.LastPicker.PreviewBrushKey);
        Assert.Equal(new NodeIcon("shield", null), store.Saved.Connections.Single(c => c.Id == conn.Id).Icon);
        Assert.Equal("Glyph.shield", vm.Tree.Roots.Single().IconKey);
    }

    [Fact]
    public async Task Cancelling_the_picker_changes_nothing()
    {
        var (vm, store, dialogs, conn) = await CreateAsync();
        var saves = store.Saves;

        await vm.ChangeIconCommand.ExecuteAsync(vm.Tree.Roots.Single());

        Assert.NotNull(dialogs.LastPicker);
        Assert.Equal(saves, store.Saves);
        Assert.Null(store.Saved.Connections.Single(c => c.Id == conn.Id).Icon);
    }
}
