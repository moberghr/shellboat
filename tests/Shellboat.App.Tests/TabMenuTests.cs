using Shellboat.App.Connections;
using Shellboat.App.ViewModels;
using Shellboat.Core.Connections;
using Shellboat.Core.Import;
using Shellboat.Core.Secrets;

namespace Shellboat.App.Tests;

public class TabMenuTests
{
    private sealed class NoImporter : ICredentialImporter
    {
        public Task<IReadOnlyList<ImportedEntry>> ReadAsync(string databasePath, string masterPassword, string? keyFilePath, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed record Setup(MainWindowViewModel Vm, FakeDialogs Dialogs, FakeSecrets Secrets, ConnectionEntry Conn);

    /// <summary>An SSH connection with a saved password, opened in one terminal tab.</summary>
    private static async Task<Setup> OpenOneTabAsync()
    {
        var secrets = new FakeSecrets();
        var tree = new ConnectionTreeViewModel(new MemoryConnectionStore(), secrets);
        var conn = new ConnectionEntry { Id = Guid.NewGuid(), Name = "web", Kind = ConnectionKind.Ssh, Host = "web", UserName = "u", Ssh = new SshSettings() };
        await tree.UpsertAsync(conn);
        secrets.Items[(conn.Id, SecretPurpose.Password)] = "pw";
        var dialogs = new FakeDialogs();
        var vm = new MainWindowViewModel(tree, secrets, dialogs,
            (_, creds) => new FakeChannel(_ => null, creds),
            (_, creds) => new FakeFileChannel(_ => null, creds),
            new NoImporter())
        {
            PasswordClipboardLifetime = TimeSpan.FromMinutes(5),
        };
        await vm.ConnectCommand.ExecuteAsync(vm.Tree.Roots.Single());
        return new Setup(vm, dialogs, secrets, conn);
    }

    [Fact]
    public async Task Duplicate_opens_a_second_session_of_the_same_kind_and_selects_it()
    {
        var s = await OpenOneTabAsync();
        var first = s.Vm.Tabs.Single();

        await s.Vm.DuplicateTabCommand.ExecuteAsync(first);

        Assert.Equal(2, s.Vm.Tabs.Count);
        var second = Assert.IsType<TerminalTabViewModel>(s.Vm.Tabs[1]);
        Assert.Same(second, s.Vm.SelectedTab);
        Assert.Equal(s.Conn.Id, second.Connection.Id);
    }

    [Fact]
    public async Task Duplicating_an_sftp_tab_opens_another_file_browser()
    {
        var s = await OpenOneTabAsync();
        await s.Vm.OpenSftpForTabCommand.ExecuteAsync(s.Vm.Tabs.Single());

        await s.Vm.DuplicateTabCommand.ExecuteAsync(s.Vm.Tabs[1]);

        Assert.Collection(s.Vm.Tabs,
            t => Assert.IsType<TerminalTabViewModel>(t),
            t => Assert.IsType<SftpTabViewModel>(t),
            t => Assert.IsType<SftpTabViewModel>(t));
    }

    [Fact]
    public async Task Duplicate_uses_the_connection_as_edited_since_the_tab_opened()
    {
        var s = await OpenOneTabAsync();
        await s.Vm.Tree.UpsertAsync(s.Conn with { Name = "web (renamed)" });

        await s.Vm.DuplicateTabCommand.ExecuteAsync(s.Vm.Tabs.Single());

        Assert.Equal("web (renamed)", s.Vm.Tabs[1].Title);
    }

    [Fact]
    public async Task Copy_password_from_a_tab_copies_the_connections_saved_password()
    {
        var s = await OpenOneTabAsync();

        await s.Vm.CopyTabPasswordCommand.ExecuteAsync(s.Vm.Tabs.Single());

        Assert.Equal("pw", s.Dialogs.Clipboard);
    }

    [Fact]
    public async Task Rename_changes_only_the_tab_and_an_empty_name_restores_the_connection_name()
    {
        var s = await OpenOneTabAsync();
        var tab = s.Vm.Tabs.Single();

        s.Dialogs.TextAnswers.Enqueue("  prod shell ");
        await s.Vm.RenameTabCommand.ExecuteAsync(tab);
        Assert.Equal("prod shell", tab.Title);
        Assert.Equal("web", s.Vm.Tree.Document.Connections.Single().Name);

        s.Dialogs.TextAnswers.Enqueue("");
        await s.Vm.RenameTabCommand.ExecuteAsync(tab);
        Assert.Equal("web", tab.Title);
    }

    [Fact]
    public async Task Cancelling_rename_keeps_the_title()
    {
        var s = await OpenOneTabAsync();
        var tab = s.Vm.Tabs.Single();
        tab.Title = "custom";

        await s.Vm.RenameTabCommand.ExecuteAsync(tab);

        Assert.Equal("custom", tab.Title);
    }

    [Fact]
    public async Task Tab_color_options_set_and_clear_the_strip()
    {
        var s = await OpenOneTabAsync();
        var tab = s.Vm.Tabs.Single();

        tab.TabColorOptions.Single(o => o.Label == "Red").Apply.Execute(null);
        Assert.Equal("#D2555A", tab.TabColor);
        Assert.True(tab.HasTabColor);

        tab.TabColorOptions.Single(o => o.Label == "None").Apply.Execute(null);
        Assert.Null(tab.TabColor);
        Assert.False(tab.HasTabColor);
    }

    [Fact]
    public async Task Changing_the_icon_from_a_tab_saves_it_on_the_connection_and_updates_its_tabs()
    {
        var s = await OpenOneTabAsync();
        await s.Vm.DuplicateTabCommand.ExecuteAsync(s.Vm.Tabs.Single());
        s.Dialogs.IconChoice = p => p.SelectedGlyph = p.Glyphs.Single(g => g.Name == "shield");

        await s.Vm.ChangeTabIconCommand.ExecuteAsync(s.Vm.Tabs[0]);

        var saved = s.Vm.Tree.Document.Connections.Single().Icon;
        Assert.Equal("shield", saved?.Glyph);
        Assert.All(s.Vm.Tabs, t => Assert.Equal(IconCatalog.GlyphKey(saved, "unused"), t.IconKey));
    }

    [Fact]
    public async Task Changing_the_icon_in_the_tree_updates_open_tabs_too()
    {
        var s = await OpenOneTabAsync();
        s.Dialogs.IconChoice = p => p.SelectedGlyph = p.Glyphs.Single(g => g.Name == "shield");

        await s.Vm.ChangeIconCommand.ExecuteAsync(s.Vm.Tree.Roots.Single());

        Assert.Equal("shield", s.Vm.Tabs.Single().Icon?.Glyph);
    }

    [Fact]
    public async Task Show_in_tree_clears_the_filter_and_selects_the_connection()
    {
        var s = await OpenOneTabAsync();
        s.Vm.Tree.Filter = "nothing-matches";

        s.Vm.ShowTabInTreeCommand.Execute(s.Vm.Tabs.Single());

        Assert.Equal("", s.Vm.Tree.Filter);
        Assert.Equal(s.Conn.Id, s.Vm.Tree.Selected?.Id);
    }

    [Fact]
    public async Task Close_other_tabs_keeps_only_the_clicked_one()
    {
        var s = await OpenOneTabAsync();
        var keep = s.Vm.Tabs.Single();
        await s.Vm.DuplicateTabCommand.ExecuteAsync(keep);
        await s.Vm.OpenSftpForTabCommand.ExecuteAsync(keep);

        await s.Vm.CloseOtherTabsCommand.ExecuteAsync(keep);

        Assert.Same(keep, s.Vm.Tabs.Single());
        Assert.Same(keep, s.Vm.SelectedTab);
    }
}

public class TabMoveAndDetachTests
{
    private sealed class NoImporter : ICredentialImporter
    {
        public Task<IReadOnlyList<ImportedEntry>> ReadAsync(string databasePath, string masterPassword, string? keyFilePath, CancellationToken ct)
            => throw new NotSupportedException();
    }

    /// <summary>Three terminal tabs a, b, c to the same connection, titled for readability.</summary>
    private static Task<MainWindowViewModel> ThreeTabsAsync() => ThreeTabsAsync([]);

    private static async Task<MainWindowViewModel> ThreeTabsAsync(List<FakeChannel> channels)
    {
        var secrets = new FakeSecrets();
        var tree = new ConnectionTreeViewModel(new MemoryConnectionStore(), secrets);
        var conn = new ConnectionEntry { Id = Guid.NewGuid(), Name = "web", Kind = ConnectionKind.Ssh, Host = "web", UserName = "u", Ssh = new SshSettings() };
        await tree.UpsertAsync(conn);
        secrets.Items[(conn.Id, SecretPurpose.Password)] = "pw";
        var vm = new MainWindowViewModel(tree, secrets, new FakeDialogs(),
            (_, creds) =>
            {
                var channel = new FakeChannel(_ => null, creds);
                channels.Add(channel);
                return channel;
            },
            (_, creds) => new FakeFileChannel(_ => null, creds),
            new NoImporter());
        foreach (var title in new[] { "a", "b", "c" })
        {
            await vm.ConnectCommand.ExecuteAsync(vm.Tree.Roots.Single());
            vm.Tabs[^1].Title = title;
        }

        return vm;
    }

    private static string Order(MainWindowViewModel vm) => string.Concat(vm.Tabs.Select(t => t.Title));

    [Theory]
    [InlineData("a", 2, "bca")]
    [InlineData("c", 0, "cab")]
    [InlineData("b", 1, "abc")]
    [InlineData("a", 99, "bca")]
    [InlineData("c", -3, "cab")]
    public async Task MoveTab_reorders_clamps_and_keeps_the_dragged_tab_selected(string title, int to, string expected)
    {
        var vm = await ThreeTabsAsync();
        var tab = vm.Tabs.Single(t => t.Title == title);

        vm.MoveTab(tab, to);

        Assert.Equal(expected, Order(vm));
        Assert.Same(tab, vm.SelectedTab);
    }

    [Fact]
    public async Task Detach_moves_the_live_session_out_of_the_strip_and_selects_a_neighbour()
    {
        var vm = await ThreeTabsAsync();
        var b = vm.Tabs[1];
        vm.SelectedTab = b;

        vm.DetachTabCommand.Execute(b);

        Assert.Equal("ac", Order(vm));
        Assert.Same(b, vm.DetachedTabs.Single());
        Assert.Equal("c", vm.SelectedTab?.Title);
        Assert.True(b.IsConnected); // detaching never reconnects or disposes
    }

    [Fact]
    public async Task Attach_brings_it_back_as_the_last_selected_tab()
    {
        var vm = await ThreeTabsAsync();
        var a = vm.Tabs[0];
        vm.DetachTabCommand.Execute(a);

        vm.AttachTabCommand.Execute(a);

        Assert.Equal("bca", Order(vm));
        Assert.Empty(vm.DetachedTabs);
        Assert.Same(a, vm.SelectedTab);
        Assert.True(a.IsConnected);
    }

    [Fact]
    public async Task Closing_a_detached_session_disposes_its_channel()
    {
        var channels = new List<FakeChannel>();
        var vm = await ThreeTabsAsync(channels);
        var a = vm.Tabs[0];
        vm.DetachTabCommand.Execute(a);
        Assert.False(channels[0].Disposed);

        await vm.CloseTabCommand.ExecuteAsync(a);

        Assert.Empty(vm.DetachedTabs);
        Assert.Equal("bc", Order(vm));
        Assert.True(channels[0].Disposed);
        Assert.All(channels.Skip(1), c => Assert.False(c.Disposed));
    }

    [Fact]
    public async Task Typing_exit_in_a_detached_session_closes_it()
    {
        var vm = await ThreeTabsAsync();
        var a = vm.Tabs[0];
        vm.DetachTabCommand.Execute(a);

        a.CloseCommand.Execute(null);
        await Task.Yield();

        Assert.Empty(vm.DetachedTabs);
    }
}
