using Avalonia.Input;
using Shellboat.App.Services;
using Shellboat.App.Settings;
using Shellboat.App.Terminal;
using Shellboat.App.ViewModels;
using Shellboat.Core.Connections;
using Shellboat.Core.Import;
using Shellboat.Core.Secrets;
using Shellboat.Core.Settings;

namespace Shellboat.App.Tests;

internal sealed class MemorySettingsStore : ISettingsStore
{
    public AppSettings? Saved { get; private set; }

    public Task<AppSettings> LoadAsync(CancellationToken ct) => Task.FromResult(Saved ?? AppSettings.Default);

    public Task SaveAsync(AppSettings settings, CancellationToken ct)
    {
        Saved = settings;
        return Task.CompletedTask;
    }
}

public class SettingsViewModelTests
{
    private static ShortcutRowViewModel Row(SettingsViewModel vm, string id) => vm.Shortcuts.Single(r => r.Action.Id == id);

    [Fact]
    public void Untouched_settings_build_back_unchanged_with_no_shortcut_overrides()
    {
        var vm = new SettingsViewModel(AppSettings.Default, isMac: true);

        var built = vm.Build();

        Assert.Empty(built.Shortcuts);
        Assert.Equal(AppSettings.Default with { Shortcuts = built.Shortcuts }, built);
    }

    [Fact]
    public void Recording_a_chord_rebinds_the_row_and_is_saved_as_an_override()
    {
        var vm = new SettingsViewModel(AppSettings.Default, isMac: true);
        var close = Row(vm, ShortcutCatalog.CloseTab);

        vm.RecordCommand.Execute(close);
        Assert.True(vm.HandleKey(Key.LeftShift, KeyModifiers.Shift));   // lone modifier: still recording
        Assert.True(close.IsRecording);
        Assert.True(vm.HandleKey(Key.K, KeyModifiers.Meta | KeyModifiers.Shift));

        Assert.False(close.IsRecording);
        var saved = vm.Build().Shortcuts[ShortcutCatalog.CloseTab];
        var parsed = ShortcutMap.TryParse(saved);
        Assert.Equal(Key.K, parsed?.Key);
        Assert.Equal(KeyModifiers.Meta | KeyModifiers.Shift, parsed?.KeyModifiers);
    }

    [Fact]
    public void Escape_cancels_recording_and_backspace_unbinds()
    {
        var vm = new SettingsViewModel(AppSettings.Default, isMac: true);
        var close = Row(vm, ShortcutCatalog.CloseTab);

        vm.RecordCommand.Execute(close);
        vm.HandleKey(Key.Escape, KeyModifiers.None);
        Assert.True(close.IsDefault);

        vm.RecordCommand.Execute(close);
        vm.HandleKey(Key.Back, KeyModifiers.None);
        Assert.Null(close.Gesture);
        Assert.Equal("", vm.Build().Shortcuts[ShortcutCatalog.CloseTab]);
    }

    [Fact]
    public void Keys_are_ignored_when_no_row_is_recording()
        => Assert.False(new SettingsViewModel(AppSettings.Default, isMac: true).HandleKey(Key.K, KeyModifiers.Meta));

    [Fact]
    public void A_duplicate_gesture_blocks_saving_until_resolved()
    {
        var vm = new SettingsViewModel(AppSettings.Default, isMac: true);
        var reconnect = Row(vm, ShortcutCatalog.Reconnect);

        vm.RecordCommand.Execute(reconnect);
        vm.HandleKey(Key.W, KeyModifiers.Meta); // = Close tab

        Assert.False(vm.CanSave);
        Assert.Contains("Close tab", reconnect.Problem);
        Assert.Contains("Reconnect", Row(vm, ShortcutCatalog.CloseTab).Problem);

        vm.ResetShortcutCommand.Execute(reconnect);
        Assert.True(vm.CanSave);
        Assert.Null(reconnect.Problem);
    }

    [Fact]
    public void Plain_ctrl_letter_on_windows_warns_but_does_not_block()
    {
        var vm = new SettingsViewModel(AppSettings.Default, isMac: false);
        var close = Row(vm, ShortcutCatalog.CloseTab);

        vm.RecordCommand.Execute(close);
        vm.HandleKey(Key.W, KeyModifiers.Control);

        Assert.True(vm.CanSave);
        Assert.Contains("terminal", close.Problem);
    }

    [Fact]
    public void Reset_all_restores_every_default()
    {
        var vm = new SettingsViewModel(AppSettings.Default with
        {
            Shortcuts = new Dictionary<string, string> { [ShortcutCatalog.CloseTab] = "", [ShortcutCatalog.Reconnect] = "Meta+K" },
        }, isMac: true);

        vm.ResetAllShortcutsCommand.Execute(null);

        Assert.All(vm.Shortcuts, r => Assert.True(r.IsDefault));
        Assert.Empty(vm.Build().Shortcuts);
    }

    [Fact]
    public void Edited_fields_are_built_and_clamped()
    {
        var vm = new SettingsViewModel(AppSettings.Default, isMac: true)
        {
            Theme = ThemeMode.Light,
            TerminalColors = TerminalColorMode.Dark,
            TerminalFontFamily = "  ",
            TerminalFontSize = 99,
            PasswordClipboardSeconds = 45,
            ConfirmCloseLiveSessions = false,
        };

        var built = vm.Build();

        Assert.Equal(ThemeMode.Light, built.Theme);
        Assert.Equal(TerminalColorMode.Dark, built.TerminalColors);
        Assert.Null(built.TerminalFontFamily);
        Assert.Equal(AppSettings.MaxFontSize, built.TerminalFontSize);
        Assert.Equal(45, built.PasswordClipboardSeconds);
        Assert.False(built.ConfirmCloseLiveSessions);
    }
}

public class SettingsSectionTests
{
    [Fact]
    public void Opens_on_appearance_by_default_and_on_the_requested_page_otherwise()
    {
        Assert.True(new SettingsViewModel(AppSettings.Default, true).ShowAppearance);

        var keyboard = new SettingsViewModel(AppSettings.Default, true, SettingsViewModel.KeyboardSection);
        Assert.True(keyboard.ShowKeyboard);
        Assert.False(keyboard.ShowAppearance);
    }

    [Fact]
    public void Exactly_one_page_is_visible_and_a_deselect_falls_back_to_the_first()
    {
        var vm = new SettingsViewModel(AppSettings.Default, true);
        foreach (var section in vm.Sections)
        {
            vm.SelectedSection = section;
            Assert.Equal(1, new[] { vm.ShowAppearance, vm.ShowTerminal, vm.ShowKeyboard, vm.ShowSessions }.Count(v => v));
        }

        vm.SelectedSection = null!;
        Assert.Same(vm.Sections[0], vm.SelectedSection);
    }

    [Fact]
    public void Theme_tiles_and_the_theme_value_stay_in_step()
    {
        var vm = new SettingsViewModel(AppSettings.Default with { Theme = ThemeMode.Dark }, true);
        Assert.Equal(ThemeMode.Dark, vm.SelectedTheme.Value);

        vm.SelectedTheme = vm.Themes.Single(t => t.Value == ThemeMode.Light);

        Assert.Equal(ThemeMode.Light, vm.Build().Theme);
    }
}

public class TerminalThemeTests
{
    [Theory]
    [InlineData(TerminalColorMode.FollowTheme, true, false)]
    [InlineData(TerminalColorMode.FollowTheme, false, true)]
    [InlineData(TerminalColorMode.Light, true, true)]
    [InlineData(TerminalColorMode.Dark, false, false)]
    public void Terminal_colours_follow_the_app_unless_forced(TerminalColorMode mode, bool appIsDark, bool expectLight)
        => Assert.Same(expectLight ? TerminalTheme.Light : TerminalTheme.Dark, TerminalTheme.Resolve(mode, appIsDark));

    [Fact]
    public void Ansi_colours_come_from_the_theme_and_the_cube_from_the_shared_palette()
    {
        Assert.NotEqual(TerminalTheme.Light.Rgb(3), TerminalTheme.Dark.Rgb(3));
        Assert.Equal(TerminalTheme.Light.Rgb(200), TerminalTheme.Dark.Rgb(200));
        Assert.Equal(16, TerminalTheme.Light.Ansi.Count);
    }

    [Fact]
    public void Light_foreground_is_dark_and_background_is_light()
    {
        static double Luma(uint rgb) => 0.299 * (rgb >> 16 & 0xFF) + 0.587 * (rgb >> 8 & 0xFF) + 0.114 * (rgb & 0xFF);

        Assert.True(Luma(TerminalTheme.Light.Background) > 200);
        Assert.True(Luma(TerminalTheme.Light.Foreground) < 80);
    }
}

public class TerminalBackgroundTests
{
    [Theory]
    [InlineData("#2b2b2b", "#2B2B2B")]
    [InlineData(" #1E1E1E ", "#1E1E1E")]
    [InlineData("2B2B2B", null)]
    [InlineData("#2B2B2", null)]
    [InlineData("#GGGGGG", null)]
    [InlineData("", null)]
    public void Only_hex_colours_survive_normalisation(string stored, string? expected)
        => Assert.Equal(expected, (AppSettings.Default with { TerminalBackground = stored }).Normalize().TerminalBackground);

    [Fact]
    public void A_dark_custom_background_keeps_the_dark_palette_even_when_forced_light()
    {
        var theme = TerminalTheme.Resolve(TerminalColorMode.Light, appIsDark: false, "#2B2B2B");

        Assert.Equal(0x2B2B2Bu, theme.Background);
        Assert.Equal(TerminalTheme.Dark.Foreground, theme.Foreground);
        Assert.Equal(TerminalTheme.Dark.Ansi, theme.Ansi);
    }

    [Fact]
    public void A_light_custom_background_uses_the_light_palette()
    {
        var theme = TerminalTheme.Resolve(TerminalColorMode.Dark, appIsDark: true, "#F0EAD6");

        Assert.Equal(0xF0EAD6u, theme.Background);
        Assert.Equal(TerminalTheme.Light.Foreground, theme.Foreground);
    }

    [Fact]
    public void No_or_bad_custom_background_falls_back_to_the_mode()
    {
        Assert.Same(TerminalTheme.Dark, TerminalTheme.Resolve(TerminalColorMode.Dark, false, null));
        Assert.Same(TerminalTheme.Light, TerminalTheme.Resolve(TerminalColorMode.Light, true, "nope"));
    }

    [Fact]
    public void Picker_colour_is_saved_only_while_custom_is_on()
    {
        var vm = new SettingsViewModel(AppSettings.Default, isMac: true);
        Assert.False(vm.UseCustomTerminalBackground);
        Assert.Null(vm.Build().TerminalBackground);

        vm.TerminalBackgroundColor = Avalonia.Media.Color.FromRgb(0x2A, 0x2D, 0x31);
        Assert.Null(vm.Build().TerminalBackground);

        vm.UseCustomTerminalBackground = true;
        Assert.Equal("#2A2D31", vm.Build().TerminalBackground);
    }

    [Fact]
    public void A_preset_switches_custom_on_and_reopens_with_the_saved_colour()
    {
        var vm = new SettingsViewModel(AppSettings.Default, isMac: true);
        vm.ApplyBackgroundPresetCommand.Execute(vm.BackgroundPresets.Single(p => p.Label == "Slate"));

        var saved = vm.Build();
        Assert.Equal("#282C34", saved.TerminalBackground);

        var reopened = new SettingsViewModel(saved, isMac: true);
        Assert.True(reopened.UseCustomTerminalBackground);
        Assert.Equal("#282C34", reopened.TerminalBackgroundHex);
    }
}

public class ShortcutDispatchTests
{
    private sealed class NoImporter : ICredentialImporter
    {
        public Task<IReadOnlyList<ImportedEntry>> ReadAsync(string databasePath, string masterPassword, string? keyFilePath, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed record Setup(MainWindowViewModel Vm, FakeDialogs Dialogs, MemorySettingsStore Store);

    private static async Task<Setup> CreateAsync(int tabs = 2, AppSettings? settings = null, Func<DateTime>? clock = null)
    {
        var secrets = new FakeSecrets();
        var tree = new ConnectionTreeViewModel(new MemoryConnectionStore(), secrets);
        var conn = new ConnectionEntry { Id = Guid.NewGuid(), Name = "web", Kind = ConnectionKind.Ssh, Host = "web", UserName = "u", Ssh = new SshSettings() };
        await tree.UpsertAsync(conn);
        secrets.Items[(conn.Id, SecretPurpose.Password)] = "pw";
        var dialogs = new FakeDialogs();
        var store = new MemorySettingsStore();
        var vm = new MainWindowViewModel(tree, secrets, dialogs,
            (_, creds) => new FakeChannel(_ => null, creds),
            (_, creds) => new FakeFileChannel(_ => null, creds),
            new NoImporter(),
            settings: new SettingsService(store, settings))
        {
            Clock = clock ?? (() => DateTime.UtcNow),
        };
        for (var i = 0; i < tabs; i++)
        {
            await vm.ConnectCommand.ExecuteAsync(vm.Tree.Roots.Single());
            vm.Tabs[^1].Title = ((char)('a' + i)).ToString();
        }

        return new Setup(vm, dialogs, store);
    }

    [Fact]
    public async Task Next_and_previous_tab_wrap_around()
    {
        var s = await CreateAsync(tabs: 3);
        s.Vm.SelectedTab = s.Vm.Tabs[2];

        await s.Vm.RunActionAsync(ShortcutCatalog.NextTab);
        Assert.Equal("a", s.Vm.SelectedTab?.Title);

        await s.Vm.RunActionAsync(ShortcutCatalog.PreviousTab);
        Assert.Equal("c", s.Vm.SelectedTab?.Title);
    }

    [Fact]
    public async Task Close_tab_acts_on_the_window_s_tab_when_given()
    {
        var s = await CreateAsync(tabs: 2);
        s.Dialogs.ConfirmAnswer = true;
        var detached = s.Vm.Tabs[0];
        s.Vm.DetachTabCommand.Execute(detached);

        await s.Vm.RunActionAsync(ShortcutCatalog.CloseTab, detached);

        Assert.Empty(s.Vm.DetachedTabs);
        Assert.Single(s.Vm.Tabs);
    }

    [Fact]
    public async Task Zoom_changes_and_saves_the_terminal_font_size()
    {
        var s = await CreateAsync(tabs: 0);

        await s.Vm.RunActionAsync(ShortcutCatalog.ZoomIn);
        await s.Vm.RunActionAsync(ShortcutCatalog.ZoomIn);
        Assert.Equal(15, s.Store.Saved?.TerminalFontSize);

        await s.Vm.RunActionAsync(ShortcutCatalog.ZoomReset);
        Assert.Equal(13, s.Vm.Settings.Current.TerminalFontSize);
    }

    [Fact]
    public async Task The_same_action_from_menu_right_after_the_key_runs_once()
    {
        var now = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        var s = await CreateAsync(tabs: 0, clock: () => now);

        await s.Vm.RunActionAsync(ShortcutCatalog.ZoomIn, null, CommandSource.Keyboard);
        await s.Vm.RunActionAsync(ShortcutCatalog.ZoomIn, null, CommandSource.Menu);
        Assert.Equal(14, s.Vm.Settings.Current.TerminalFontSize);

        now = now.AddSeconds(1);
        await s.Vm.RunActionAsync(ShortcutCatalog.ZoomIn, null, CommandSource.Menu);
        Assert.Equal(15, s.Vm.Settings.Current.TerminalFontSize);
    }

    [Fact]
    public async Task Repeated_key_presses_are_not_deduplicated()
    {
        var now = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        var s = await CreateAsync(tabs: 0, clock: () => now);

        await s.Vm.RunActionAsync(ShortcutCatalog.ZoomIn);
        await s.Vm.RunActionAsync(ShortcutCatalog.ZoomIn);

        Assert.Equal(15, s.Vm.Settings.Current.TerminalFontSize);
    }

    [Fact]
    public async Task Unknown_actions_report_false()
        => Assert.False(await (await CreateAsync(tabs: 0)).Vm.RunActionAsync("no-such-action"));

    [Fact]
    public async Task Focus_filter_raises_the_view_event()
    {
        var s = await CreateAsync(tabs: 0);
        var raised = false;
        s.Vm.FocusFilterRequested += (_, _) => raised = true;

        await s.Vm.RunActionAsync(ShortcutCatalog.FocusFilter);

        Assert.True(raised);
    }

    [Fact]
    public async Task Saving_settings_applies_them_and_rebuilds_the_shortcut_map()
    {
        var s = await CreateAsync(tabs: 0);
        s.Dialogs.SettingsChoice = editor =>
        {
            editor.Theme = ThemeMode.Light;
            editor.RecordCommand.Execute(editor.Shortcuts.Single(r => r.Action.Id == ShortcutCatalog.Reconnect));
            editor.HandleKey(Key.F5, KeyModifiers.None);
        };

        await s.Vm.RunActionAsync(ShortcutCatalog.Settings);

        Assert.Equal(ThemeMode.Light, s.Store.Saved?.Theme);
        Assert.Equal(ShortcutCatalog.Reconnect, s.Vm.Settings.Shortcuts.Match(Key.F5, KeyModifiers.None));
    }

    [Fact]
    public async Task Help_keyboard_shortcuts_opens_settings_on_the_keyboard_page()
    {
        var s = await CreateAsync(tabs: 0);
        SettingsViewModel? opened = null;
        s.Dialogs.SettingsChoice = editor => opened = editor;

        await s.Vm.OpenSettingsCommand.ExecuteAsync(SettingsViewModel.KeyboardSection);

        Assert.True(opened?.ShowKeyboard);
    }

    [Fact]
    public async Task Cancelling_settings_changes_nothing()
    {
        var s = await CreateAsync(tabs: 0);

        await s.Vm.RunActionAsync(ShortcutCatalog.Settings);

        Assert.Null(s.Store.Saved);
    }

    [Fact]
    public async Task The_password_clipboard_time_comes_from_settings()
    {
        var s = await CreateAsync(tabs: 0, settings: AppSettings.Default with { PasswordClipboardSeconds = 90 });

        Assert.Equal(TimeSpan.FromSeconds(90), s.Vm.PasswordClipboardLifetime);
    }

    [Fact]
    public async Task New_terminals_use_the_scrollback_setting()
    {
        var s = await CreateAsync(tabs: 1, settings: AppSettings.Default with { ScrollbackLines = 150 });
        var terminal = (TerminalTabViewModel)s.Vm.Tabs.Single();
        for (var i = 0; i < 1000; i++)
        {
            terminal.Emulator.Feed($"{i}\r\n");
        }

        terminal.Emulator.ScrollView(-100_000);

        Assert.Equal(150, terminal.Emulator.Snapshot().ScrollOffset);
    }
}

public class ConfirmCloseTests
{
    private sealed class NoImporter : ICredentialImporter
    {
        public Task<IReadOnlyList<ImportedEntry>> ReadAsync(string databasePath, string masterPassword, string? keyFilePath, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private static async Task<(MainWindowViewModel Vm, FakeDialogs Dialogs, List<FakeChannel> Channels)> CreateAsync(bool confirm, int tabs = 1)
    {
        var secrets = new FakeSecrets();
        var tree = new ConnectionTreeViewModel(new MemoryConnectionStore(), secrets);
        var conn = new ConnectionEntry { Id = Guid.NewGuid(), Name = "web", Kind = ConnectionKind.Ssh, Host = "web", UserName = "u", Ssh = new SshSettings() };
        await tree.UpsertAsync(conn);
        secrets.Items[(conn.Id, SecretPurpose.Password)] = "pw";
        var dialogs = new FakeDialogs();
        var channels = new List<FakeChannel>();
        var vm = new MainWindowViewModel(tree, secrets, dialogs,
            (_, creds) =>
            {
                var c = new FakeChannel(_ => null, creds);
                channels.Add(c);
                return c;
            },
            (_, creds) => new FakeFileChannel(_ => null, creds),
            new NoImporter(),
            settings: new SettingsService(null, AppSettings.Default with { ConfirmCloseLiveSessions = confirm }));
        for (var i = 0; i < tabs; i++)
        {
            await vm.ConnectCommand.ExecuteAsync(vm.Tree.Roots.Single());
        }

        return (vm, dialogs, channels);
    }

    [Fact]
    public async Task Saying_no_keeps_a_live_session_open()
    {
        var (vm, dialogs, channels) = await CreateAsync(confirm: true);
        dialogs.ConfirmAnswer = false;

        await vm.CloseTabCommand.ExecuteAsync(vm.Tabs.Single());

        Assert.Single(vm.Tabs);
        Assert.False(channels[0].Disposed);
    }

    [Fact]
    public async Task With_confirmation_off_a_live_session_closes_without_asking()
    {
        var (vm, dialogs, _) = await CreateAsync(confirm: false);
        dialogs.ConfirmAnswer = false; // would refuse if asked

        await vm.CloseTabCommand.ExecuteAsync(vm.Tabs.Single());

        Assert.Empty(vm.Tabs);
    }

    [Fact]
    public async Task A_session_that_ended_by_itself_closes_without_asking()
    {
        var (vm, dialogs, _) = await CreateAsync(confirm: true);
        dialogs.ConfirmAnswer = false;

        vm.Tabs.Single().CloseCommand.Execute(null); // what `exit` raises
        await Task.Yield();

        Assert.Empty(vm.Tabs);
    }

    [Fact]
    public async Task Close_other_tabs_asks_once_and_no_keeps_them_all()
    {
        var (vm, dialogs, channels) = await CreateAsync(confirm: true, tabs: 3);
        dialogs.ConfirmAnswer = false;

        await vm.CloseOtherTabsCommand.ExecuteAsync(vm.Tabs[0]);

        Assert.Equal(3, vm.Tabs.Count);
        Assert.All(channels, c => Assert.False(c.Disposed));
    }

    [Fact]
    public async Task NeedsCloseConfirmation_reflects_live_sessions_and_the_setting()
    {
        var (on, _, _) = await CreateAsync(confirm: true);
        var (off, _, _) = await CreateAsync(confirm: false);

        Assert.True(on.NeedsCloseConfirmation(on.AllSessions));
        Assert.False(off.NeedsCloseConfirmation(off.AllSessions));
        Assert.False(on.NeedsCloseConfirmation([]));
    }
}
