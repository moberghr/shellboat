using System.Collections.ObjectModel;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shellboat.App.Settings;
using Shellboat.Core.Settings;

namespace Shellboat.App.ViewModels;

/// <summary>A theme tile: label plus the window/chrome/text colours its little preview draws (two halves for
/// "Follow system").</summary>
public sealed record ThemeOption(string Label, ThemeMode Value, string LeftBg, string RightBg, string LeftText, string RightText);

/// <summary>A page of the Settings window (the tab bar).</summary>
public sealed record SettingsSection(string Key, string Label, string IconKey);

/// <summary>A labelled choice for a combo box.</summary>
public sealed record TerminalColorOption(string Label, TerminalColorMode Value);

/// <summary>A ready-made terminal background ("#RRGGBB").</summary>
public sealed record BackgroundPreset(string Label, string Hex);

/// <summary>One action in Settings → Keyboard.</summary>
public sealed partial class ShortcutRowViewModel(ShortcutAction action, KeyGesture? gesture, KeyGesture? defaultGesture, bool isMac) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display), nameof(IsDefault))]
    private KeyGesture? _gesture = gesture;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private bool _isRecording;

    [ObservableProperty]
    private string? _problem;

    public ShortcutAction Action { get; } = action;

    public string Label => Action.Label;

    public string Group => Action.Group;

    public KeyGesture? DefaultGesture { get; } = defaultGesture;

    public string Display => IsRecording ? "Press keys… (Esc cancels, Backspace clears)" : ShortcutMap.Display(Gesture, isMac);

    public bool IsDefault => Same(Gesture, DefaultGesture);

    internal static bool Same(KeyGesture? a, KeyGesture? b)
        => a is null ? b is null : b is not null && a.Key == b.Key && a.KeyModifiers == b.KeyModifiers;
}

/// <summary>Settings window: edits a copy; <see cref="Build"/> is applied only when the user saves.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _original;
    private readonly bool _isMac;

    [ObservableProperty]
    private ThemeMode _theme;

    [ObservableProperty]
    private TerminalColorMode _terminalColors;

    /// <summary>Off = the colour mode's own background.</summary>
    [ObservableProperty]
    private bool _useCustomTerminalBackground;

    [ObservableProperty]
    private Color _terminalBackgroundColor;

    /// <summary>Empty = the built-in monospace list.</summary>
    [ObservableProperty]
    private string _terminalFontFamily;

    [ObservableProperty]
    private double _terminalFontSize;

    [ObservableProperty]
    private bool _copyOnSelect;

    [ObservableProperty]
    private bool _rightClickPaste;

    [ObservableProperty]
    private int _scrollbackLines;

    [ObservableProperty]
    private int _passwordClipboardSeconds;

    [ObservableProperty]
    private bool _confirmCloseLiveSessions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string? _shortcutProblem;

    /// <param name="section">Page to open on (<see cref="KeyboardSection"/> for Help → Keyboard Shortcuts).</param>
    public SettingsViewModel(AppSettings current, bool isMac, string? section = null)
    {
        _selectedSection = Sections.FirstOrDefault(x => x.Key == section) ?? Sections[0];
        _original = current.Normalize();
        _isMac = isMac;
        _theme = _original.Theme;
        _terminalColors = _original.TerminalColors;
        _useCustomTerminalBackground = _original.TerminalBackground is not null;
        _terminalBackgroundColor = Color.Parse(_original.TerminalBackground ?? DefaultCustomBackground);
        _terminalFontFamily = _original.TerminalFontFamily ?? "";
        _terminalFontSize = _original.TerminalFontSize;
        _copyOnSelect = _original.CopyOnSelect;
        _rightClickPaste = _original.RightClickPaste;
        _scrollbackLines = _original.ScrollbackLines;
        _passwordClipboardSeconds = _original.PasswordClipboardSeconds;
        _confirmCloseLiveSessions = _original.ConfirmCloseLiveSessions;

        var map = new ShortcutMap(_original.Shortcuts, isMac);
        var defaults = new ShortcutMap(new Dictionary<string, string>(), isMac);
        foreach (var action in ShortcutCatalog.Actions)
        {
            Shortcuts.Add(new ShortcutRowViewModel(action, map.GestureFor(action.Id), defaults.GestureFor(action.Id), isMac));
        }

        Validate();
    }

    public IReadOnlyList<ThemeOption> Themes { get; } =
    [
        new("Follow system", ThemeMode.System, "#F6F8FA", "#0F1319", "#1F262E", "#D8DEE6"),
        new("Light", ThemeMode.Light, "#F6F8FA", "#ECEFF3", "#1F262E", "#1F262E"),
        new("Dark", ThemeMode.Dark, "#0F1319", "#161B21", "#D8DEE6", "#D8DEE6"),
    ];

    public const string AppearanceSection = "appearance";
    public const string TerminalSection = "terminal";
    public const string KeyboardSection = "keyboard";
    public const string SessionsSection = "sessions";

    public IReadOnlyList<SettingsSection> Sections { get; } =
    [
        new(AppearanceSection, "Appearance", "Icon.Appearance"),
        new(TerminalSection, "Terminal", "Glyph.terminal"),
        new(KeyboardSection, "Keyboard", "Icon.Keyboard"),
        new(SessionsSection, "Sessions", "Glyph.lock"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAppearance), nameof(ShowTerminal), nameof(ShowKeyboard), nameof(ShowSessions))]
    private SettingsSection _selectedSection = null!;

    public bool ShowAppearance => SelectedSection.Key == AppearanceSection;

    public bool ShowTerminal => SelectedSection.Key == TerminalSection;

    public bool ShowKeyboard => SelectedSection.Key == KeyboardSection;

    public bool ShowSessions => SelectedSection.Key == SessionsSection;

    /// <summary>The selected theme tile (two-way with <see cref="Theme"/>).</summary>
    public ThemeOption SelectedTheme
    {
        get => Themes.First(t => t.Value == Theme);
        set
        {
            if (value is not null)
            {
                Theme = value.Value;
            }
        }
    }

    partial void OnThemeChanged(ThemeMode value) => OnPropertyChanged(nameof(SelectedTheme));

    partial void OnSelectedSectionChanged(SettingsSection value)
    {
        if (value is null)
        {
            SelectedSection = Sections[0]; // a ListBox deselect never leaves the window blank
        }
    }

    public IReadOnlyList<TerminalColorOption> TerminalColorModes { get; } =
    [
        new("Same as the app theme", TerminalColorMode.FollowTheme),
        new("Always light", TerminalColorMode.Light),
        new("Always dark", TerminalColorMode.Dark),
    ];

    /// <summary>What the picker starts on the first time "Custom background" is switched on.</summary>
    public const string DefaultCustomBackground = "#1E1E1E";

    public IReadOnlyList<BackgroundPreset> BackgroundPresets { get; } =
    [
        new("Charcoal", "#1E1E1E"),
        new("Graphite", "#252526"),
        new("Slate", "#282C34"),
        new("Ash", "#2D2D2D"),
        new("Steel", "#33373D"),
        new("Ink", "#0F1319"),
    ];

    /// <summary>The background as stored: "#RRGGBB" when custom is on, else null.</summary>
    public string? TerminalBackgroundHex => UseCustomTerminalBackground
        ? $"#{TerminalBackgroundColor.R:X2}{TerminalBackgroundColor.G:X2}{TerminalBackgroundColor.B:X2}"
        : null;

    [RelayCommand]
    private void ApplyBackgroundPreset(BackgroundPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        TerminalBackgroundColor = Color.Parse(preset.Hex);
        UseCustomTerminalBackground = true;
    }

    public ObservableCollection<ShortcutRowViewModel> Shortcuts { get; } = [];

    /// <summary>Installed font names for the font box (filled by the view from the platform).</summary>
    public ObservableCollection<string> FontSuggestions { get; } = [];

    public ShortcutRowViewModel? RecordingRow => Shortcuts.FirstOrDefault(r => r.IsRecording);

    public bool CanSave => ShortcutProblem is null;

    public double MinFontSize => AppSettings.MinFontSize;

    public double MaxFontSize => AppSettings.MaxFontSize;

    public int MinScrollback => AppSettings.MinScrollback;

    public int MaxScrollback => AppSettings.MaxScrollback;

    public int MinClipboardSeconds => AppSettings.MinClipboardSeconds;

    public int MaxClipboardSeconds => AppSettings.MaxClipboardSeconds;

    [RelayCommand]
    private void Record(ShortcutRowViewModel? row)
    {
        foreach (var other in Shortcuts)
        {
            other.IsRecording = false;
        }

        if (row is not null)
        {
            row.IsRecording = true;
        }
    }

    [RelayCommand]
    private void Unbind(ShortcutRowViewModel? row)
    {
        if (row is not null)
        {
            row.IsRecording = false;
            row.Gesture = null;
            Validate();
        }
    }

    [RelayCommand]
    private void ResetShortcut(ShortcutRowViewModel? row)
    {
        if (row is not null)
        {
            row.IsRecording = false;
            row.Gesture = row.DefaultGesture;
            Validate();
        }
    }

    [RelayCommand]
    private void ResetAllShortcuts()
    {
        foreach (var row in Shortcuts)
        {
            row.IsRecording = false;
            row.Gesture = row.DefaultGesture;
        }

        Validate();
    }

    /// <summary>
    /// A key press while a row is recording. Esc cancels, Backspace/Delete without modifiers unbinds, anything
    /// assignable (<see cref="ShortcutMap.IsAssignable"/>) becomes the row's shortcut. Returns true when consumed.
    /// </summary>
    public bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (RecordingRow is not { } row)
        {
            return false;
        }

        if (key == Key.Escape && modifiers == KeyModifiers.None)
        {
            row.IsRecording = false;
            return true;
        }

        if (key is Key.Back or Key.Delete && modifiers == KeyModifiers.None)
        {
            Unbind(row);
            return true;
        }

        if (!ShortcutMap.IsAssignable(key, modifiers))
        {
            return true; // a lone modifier or plain typing: keep listening
        }

        row.Gesture = new KeyGesture(key, modifiers);
        row.IsRecording = false;
        Validate();
        return true;
    }

    /// <summary>The settings as edited; shortcuts are stored only where they differ from the default.</summary>
    public AppSettings Build()
    {
        var overrides = new Dictionary<string, string>();
        foreach (var row in Shortcuts.Where(r => !r.IsDefault))
        {
            overrides[row.Action.Id] = row.Gesture is null ? "" : row.Gesture.ToString();
        }

        return (_original with
        {
            Theme = Theme,
            TerminalColors = TerminalColors,
            TerminalBackground = TerminalBackgroundHex,
            TerminalFontFamily = TerminalFontFamily,
            TerminalFontSize = TerminalFontSize,
            CopyOnSelect = CopyOnSelect,
            RightClickPaste = RightClickPaste,
            ScrollbackLines = ScrollbackLines,
            PasswordClipboardSeconds = PasswordClipboardSeconds,
            ConfirmCloseLiveSessions = ConfirmCloseLiveSessions,
            Shortcuts = overrides,
        }).Normalize();
    }

    /// <summary>Marks rows that share a gesture and blocks saving until they don't. Also warns (without blocking)
    /// about plain Ctrl+letter on Windows/Linux, which the shell would never receive.</summary>
    private void Validate()
    {
        foreach (var row in Shortcuts)
        {
            row.Problem = null;
        }

        var clashes = ShortcutMap.Conflicts(Shortcuts.ToDictionary(r => r.Action.Id, r => r.Gesture));
        foreach (var (a, b) in clashes)
        {
            var rowA = Shortcuts.First(r => r.Action.Id == a);
            var rowB = Shortcuts.First(r => r.Action.Id == b);
            rowA.Problem = $"Same keys as “{rowB.Label}”";
            rowB.Problem = $"Same keys as “{rowA.Label}”";
        }

        if (!_isMac)
        {
            foreach (var row in Shortcuts.Where(r => r.Problem is null && r.Gesture is { KeyModifiers: KeyModifiers.Control, Key: >= Key.A and <= Key.Z }))
            {
                row.Problem = "The terminal will no longer receive this Ctrl key";
            }
        }

        ShortcutProblem = clashes.Count == 0 ? null : "Two actions share the same keys — change one before saving.";
    }
}
