using Avalonia.Controls;
using Shellboat.App.Settings;
using Shellboat.App.ViewModels;
using Shellboat.Core.Settings;

namespace Shellboat.App.Views;

/// <summary>
/// Builds the main menu (macOS menu bar; an in-window bar via <see cref="NativeMenuBar"/> elsewhere) from the
/// shortcut catalog, so every item shows its current shortcut. Items only dispatch to the view model.
/// <para><b>Built once per window, then updated in place</b> (<see cref="Refresh"/>): handing Avalonia.Native a new
/// <see cref="NativeMenu"/> for a window whose menu is already exported throws "The menu being updated does not
/// match" and aborts the app (crash 2026-11-05). Avoids a top-level "Edit" menu: AppKit would add its own
/// text-editing items whose ⌘C/⌘V compete with the terminal's.</para>
/// </summary>
public sealed class MainMenu
{
    private readonly MainWindowViewModel _vm;
    private readonly List<(NativeMenuItem Item, string Action)> _shortcutItems = [];
    private readonly List<(NativeMenuItem Item, ThemeMode Mode)> _themeItems = [];

    /// <param name="context">The tab the window shows (detached window), or null for the main window's selection.</param>
    public MainMenu(MainWindowViewModel vm, Func<SessionTabViewModel?> context, bool isMac, Action quit)
    {
        _vm = vm;
        Menu = Build(vm, context, isMac, quit);
        Refresh();
    }

    public NativeMenu Menu { get; }

    /// <summary>Shortcut labels and the theme checkmark from the current settings — same menu object.</summary>
    public void Refresh()
    {
        foreach (var (item, action) in _shortcutItems)
        {
            item.Gesture = _vm.Settings.Shortcuts.GestureFor(action);
        }

        foreach (var (item, mode) in _themeItems)
        {
            item.IsChecked = _vm.Settings.Current.Theme == mode;
        }
    }

    private NativeMenu Build(MainWindowViewModel vm, Func<SessionTabViewModel?> context, bool isMac, Action quit)
    {
        NativeMenuItem Action(string id, string? header = null)
        {
            var item = new NativeMenuItem(header ?? ShortcutCatalog.Find(id)?.Label ?? id);
            _shortcutItems.Add((item, id));
            item.Click += async (_, _) => await vm.RunActionAsync(id, context(), CommandSource.Menu);
            return item;
        }

        NativeMenuItem Command(string header, Func<Task> run)
        {
            var item = new NativeMenuItem(header);
            item.Click += async (_, _) => await run();
            return item;
        }

        NativeMenuItem Submenu(string header, params NativeMenuItemBase[] items)
        {
            var menu = new NativeMenu();
            foreach (var i in items)
            {
                menu.Add(i);
            }

            return new NativeMenuItem(header) { Menu = menu };
        }

        NativeMenuItem Theme(string header, ThemeMode mode)
        {
            var item = new NativeMenuItem(header) { ToggleType = MenuItemToggleType.Radio };
            _themeItems.Add((item, mode));
            item.Click += async (_, _) => await vm.SetThemeCommand.ExecuteAsync(mode);
            return item;
        }

        var file = new List<NativeMenuItemBase>
        {
            Action(ShortcutCatalog.NewConnection, "New Connection…"),
            Action(ShortcutCatalog.NewFolder, "New Folder…"),
            new NativeMenuItemSeparator(),
            Submenu("Import",
                Command("From Royal TSX / Royal TS (.rtsz)…", () => vm.ImportRoyalCommand.ExecuteAsync(null)),
                Command("From Ásbrú Connection Manager (.yml)…", () => vm.ImportAsbruCommand.ExecuteAsync(null)),
                Command("From KeePass (.kdbx)…", () => vm.ImportKeePassCommand.ExecuteAsync(null))),
        };
        if (!isMac)
        {
            // macOS has Settings and Quit in the application menu (see AppMenu).
            file.Add(new NativeMenuItemSeparator());
            file.Add(Action(ShortcutCatalog.Settings, "Settings…"));
            file.Add(new NativeMenuItemSeparator());
            file.Add(Command("Quit", () =>
            {
                quit();
                return Task.CompletedTask;
            }));
        }

        var root = new NativeMenu
        {
            Submenu("File", [.. file]),
            Submenu("Session",
                Action(ShortcutCatalog.DuplicateTab, "Duplicate Tab"),
                Action(ShortcutCatalog.OpenSftp, "Open SFTP"),
                Action(ShortcutCatalog.Reconnect, "Reconnect"),
                Action(ShortcutCatalog.CopyPassword, "Copy Password"),
                new NativeMenuItemSeparator(),
                Action(ShortcutCatalog.RenameTab, "Rename Tab…"),
                Action(ShortcutCatalog.DetachTab, "Detach Tab to Window"),
                Action(ShortcutCatalog.ShowInTree, "Show in Connection Tree"),
                new NativeMenuItemSeparator(),
                Action(ShortcutCatalog.CloseTab, "Close Tab"),
                Command("Close Other Tabs", () => vm.CloseOtherTabsCommand.ExecuteAsync(context() ?? vm.SelectedTab))),
            Submenu("View",
                Submenu("Theme",
                    Theme("Follow System", ThemeMode.System),
                    Theme("Light", ThemeMode.Light),
                    Theme("Dark", ThemeMode.Dark)),
                new NativeMenuItemSeparator(),
                Action(ShortcutCatalog.ZoomIn, "Bigger Terminal Font"),
                Action(ShortcutCatalog.ZoomOut, "Smaller Terminal Font"),
                Action(ShortcutCatalog.ZoomReset, "Default Terminal Font Size"),
                new NativeMenuItemSeparator(),
                Action(ShortcutCatalog.FocusFilter, "Filter Connections"),
                Action(ShortcutCatalog.NextTab, "Next Tab"),
                Action(ShortcutCatalog.PreviousTab, "Previous Tab")),
            Submenu("Help",
                Command("Keyboard Shortcuts…", () => vm.OpenSettingsCommand.ExecuteAsync(SettingsViewModel.KeyboardSection)),
                Command("About Shellboat", () => vm.ShowAboutCommand.ExecuteAsync(null))),
        };
        return root;
    }

    /// <summary>
    /// macOS application menu (Shellboat ▸ About / Settings…). Must be set on the Application before the first
    /// window shows — set later, AppKit keeps Avalonia's default menu. The items are created once and wired to the
    /// view model when it exists; Avalonia appends Services/Hide/Quit itself.
    /// </summary>
    public sealed class AppMenu
    {
        private readonly NativeMenuItem _settings = new("Settings…");
        private MainWindowViewModel? _vm;

        public AppMenu()
        {
            var about = new NativeMenuItem("About Shellboat");
            about.Click += async (_, _) =>
            {
                if (_vm is not null)
                {
                    await _vm.ShowAboutCommand.ExecuteAsync(null);
                }
            };
            _settings.Click += async (_, _) =>
            {
                if (_vm is not null)
                {
                    await _vm.RunActionAsync(ShortcutCatalog.Settings, null, CommandSource.Menu);
                }
            };
            Menu = new NativeMenu { about, new NativeMenuItemSeparator(), _settings };
        }

        public NativeMenu Menu { get; }

        public void Attach(MainWindowViewModel vm)
        {
            _vm = vm;
            Refresh();
            vm.Settings.PropertyChanged += (_, _) => Refresh();
        }

        private void Refresh() => _settings.Gesture = _vm?.Settings.Shortcuts.GestureFor(ShortcutCatalog.Settings);
    }
}
