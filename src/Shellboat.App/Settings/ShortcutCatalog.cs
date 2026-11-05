using Avalonia.Input;

namespace Shellboat.App.Settings;

/// <summary>One rebindable command. <see cref="Id"/> is stored in settings.json — never rename one.</summary>
/// <param name="MacDefault">Avalonia gesture text on macOS ("Meta+W"); empty = unbound.</param>
/// <param name="OtherDefault">Windows/Linux default. Ctrl+Shift chords, because plain Ctrl+letter belongs to the
/// shell (Ctrl+W deletes a word, Ctrl+R searches history…).</param>
public sealed record ShortcutAction(string Id, string Label, string Group, string MacDefault, string OtherDefault);

/// <summary>Every rebindable action, in the order the Settings list shows them.</summary>
public static class ShortcutCatalog
{
    public const string NewConnection = "new-connection";
    public const string NewFolder = "new-folder";
    public const string FocusFilter = "focus-filter";
    public const string Settings = "settings";
    public const string CloseTab = "close-tab";
    public const string NextTab = "next-tab";
    public const string PreviousTab = "previous-tab";
    public const string DuplicateTab = "duplicate-tab";
    public const string DetachTab = "detach-tab";
    public const string RenameTab = "rename-tab";
    public const string OpenSftp = "open-sftp";
    public const string Reconnect = "reconnect";
    public const string CopyPassword = "copy-password";
    public const string ShowInTree = "show-in-tree";
    public const string ZoomIn = "zoom-in";
    public const string ZoomOut = "zoom-out";
    public const string ZoomReset = "zoom-reset";

    public static IReadOnlyList<ShortcutAction> Actions { get; } =
    [
        new(NewConnection, "New connection", "Connections", "Meta+N", "Ctrl+Shift+N"),
        new(NewFolder, "New folder", "Connections", "Meta+Shift+N", "Ctrl+Alt+Shift+N"),
        new(FocusFilter, "Filter connections", "Connections", "Meta+Shift+F", "Ctrl+Shift+F"),
        new(Settings, "Settings", "App", "Meta+OemComma", "Ctrl+OemComma"),
        new(CloseTab, "Close tab", "Tabs", "Meta+W", "Ctrl+Shift+W"),
        new(NextTab, "Next tab", "Tabs", "Ctrl+Tab", "Ctrl+Tab"),
        new(PreviousTab, "Previous tab", "Tabs", "Ctrl+Shift+Tab", "Ctrl+Shift+Tab"),
        new(DuplicateTab, "Duplicate tab", "Tabs", "Meta+D", "Ctrl+Shift+D"),
        new(DetachTab, "Detach tab to window", "Tabs", "Meta+Shift+D", "Ctrl+Alt+Shift+D"),
        new(RenameTab, "Rename tab", "Tabs", "Meta+Shift+R", "Ctrl+Alt+Shift+R"),
        new(ShowInTree, "Show tab in connection tree", "Tabs", "", ""),
        new(OpenSftp, "Open SFTP for this connection", "Session", "Meta+Shift+O", "Ctrl+Shift+O"),
        new(Reconnect, "Reconnect", "Session", "Meta+R", "Ctrl+Shift+R"),
        new(CopyPassword, "Copy password", "Session", "Meta+Shift+P", "Ctrl+Shift+P"),
        new(ZoomIn, "Terminal font bigger", "View", "Meta+OemPlus", "Ctrl+Shift+OemPlus"),
        new(ZoomOut, "Terminal font smaller", "View", "Meta+OemMinus", "Ctrl+Shift+OemMinus"),
        new(ZoomReset, "Terminal font default size", "View", "Meta+D0", "Ctrl+Shift+D0"),
    ];

    public static ShortcutAction? Find(string id) => Actions.FirstOrDefault(a => a.Id == id);
}

/// <summary>
/// The effective shortcuts: catalog defaults for this OS with the user's overrides on top. Pure — tests pass
/// <c>isMac</c> instead of reading the OS.
/// </summary>
public sealed class ShortcutMap
{
    private readonly Dictionary<string, KeyGesture?> _byAction = [];

    public ShortcutMap(IReadOnlyDictionary<string, string> overrides, bool isMac)
    {
        foreach (var action in ShortcutCatalog.Actions)
        {
            var text = overrides.TryGetValue(action.Id, out var custom) ? custom : isMac ? action.MacDefault : action.OtherDefault;
            _byAction[action.Id] = TryParse(text);
        }
    }

    public static ShortcutMap ForCurrentOs(IReadOnlyDictionary<string, string> overrides) => new(overrides, OperatingSystem.IsMacOS());

    /// <summary>Null = unbound.</summary>
    public KeyGesture? GestureFor(string actionId) => _byAction.GetValueOrDefault(actionId);

    /// <summary>The action bound to this key press, if any.</summary>
    public string? Match(Key key, KeyModifiers modifiers)
    {
        foreach (var (id, gesture) in _byAction)
        {
            if (gesture is not null && gesture.Key == key && gesture.KeyModifiers == modifiers)
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>Avalonia gesture text, or null for empty/unparseable text.</summary>
    public static KeyGesture? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return KeyGesture.Parse(text);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a recorded key press may become a shortcut: a modifier chord (not Shift alone, which is just typing)
    /// or a function key. Bare keys would steal typing from the terminal; Ctrl+C/Ctrl+V-style plain Ctrl chords on
    /// Windows/Linux are allowed but the Settings view warns about them.
    /// </summary>
    public static bool IsAssignable(Key key, KeyModifiers modifiers)
    {
        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt
            or Key.RightAlt or Key.LWin or Key.RWin or Key.CapsLock)
        {
            return false;
        }

        var isFunctionKey = key is >= Key.F1 and <= Key.F24;
        var chord = (modifiers & ~KeyModifiers.Shift) != KeyModifiers.None;
        return chord || isFunctionKey;
    }

    /// <summary>Pairs of actions bound to the same gesture.</summary>
    public static IReadOnlyList<(string A, string B)> Conflicts(IReadOnlyDictionary<string, KeyGesture?> gestures)
    {
        var list = gestures.Where(kv => kv.Value is not null).ToList();
        var conflicts = new List<(string, string)>();
        for (var i = 0; i < list.Count; i++)
        {
            for (var j = i + 1; j < list.Count; j++)
            {
                if (list[i].Value!.Key == list[j].Value!.Key && list[i].Value!.KeyModifiers == list[j].Value!.KeyModifiers)
                {
                    conflicts.Add((list[i].Key, list[j].Key));
                }
            }
        }

        return conflicts;
    }

    /// <summary>The gesture as the user should read it: "⌘⇧D" on macOS, "Ctrl+Shift+D" elsewhere.</summary>
    public static string Display(KeyGesture? gesture, bool isMac)
    {
        if (gesture is null)
        {
            return "—";
        }

        var key = KeyName(gesture.Key);
        if (!isMac)
        {
            var parts = new List<string>();
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                parts.Add("Ctrl");
            }

            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                parts.Add("Alt");
            }

            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                parts.Add("Shift");
            }

            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta))
            {
                parts.Add("Win");
            }

            parts.Add(key);
            return string.Join("+", parts);
        }

        var mac = "";
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            mac += "⌃";
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            mac += "⌥";
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            mac += "⇧";
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            mac += "⌘";
        }

        return mac + key;
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemPlus => "=",
        Key.OemMinus => "-",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemQuotes => "'",
        Key.OemTilde => "`",
        Key.OemPipe => "\\",
        Key.Tab => "Tab",
        Key.Enter => "Enter",
        Key.Space => "Space",
        Key.Back => "Backspace",
        Key.Delete => "Delete",
        _ => key.ToString(),
    };
}
