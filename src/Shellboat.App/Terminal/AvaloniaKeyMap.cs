using Avalonia.Input;
using Shellboat.Terminal;

namespace Shellboat.App.Terminal;

/// <summary>Avalonia keys → terminal keys. Pure, so the mapping is testable without a window.</summary>
public static class AvaloniaKeyMap
{
    public static TerminalKey? ToTerminalKey(Key key) => key switch
    {
        Key.Enter => TerminalKey.Enter,
        Key.Tab => TerminalKey.Tab,
        Key.Back => TerminalKey.Backspace,
        Key.Escape => TerminalKey.Escape,
        Key.Up => TerminalKey.Up,
        Key.Down => TerminalKey.Down,
        Key.Left => TerminalKey.Left,
        Key.Right => TerminalKey.Right,
        Key.Home => TerminalKey.Home,
        Key.End => TerminalKey.End,
        Key.PageUp => TerminalKey.PageUp,
        Key.PageDown => TerminalKey.PageDown,
        Key.Insert => TerminalKey.Insert,
        Key.Delete => TerminalKey.Delete,
        Key.F1 => TerminalKey.F1,
        Key.F2 => TerminalKey.F2,
        Key.F3 => TerminalKey.F3,
        Key.F4 => TerminalKey.F4,
        Key.F5 => TerminalKey.F5,
        Key.F6 => TerminalKey.F6,
        Key.F7 => TerminalKey.F7,
        Key.F8 => TerminalKey.F8,
        Key.F9 => TerminalKey.F9,
        Key.F10 => TerminalKey.F10,
        Key.F11 => TerminalKey.F11,
        Key.F12 => TerminalKey.F12,
        _ => null,
    };

    public static KeyMods ToMods(KeyModifiers modifiers)
    {
        var mods = KeyMods.None;
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            mods |= KeyMods.Shift;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            mods |= KeyMods.Alt;
        }

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            mods |= KeyMods.Control;
        }

        return mods;
    }

    /// <summary>
    /// The character a Ctrl/Alt chord stands for, from the physical key, since text input does not fire for
    /// chords. Letters, digits and the punctuation C0 codes use.
    /// </summary>
    public static string? ChordCharacter(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ((char)('a' + (key - Key.A))).ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        Key.Space => " ",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe or Key.OemBackslash => "\\",
        Key.OemMinus => "-",
        Key.OemQuestion => "/",
        Key.OemPeriod => ".",
        Key.OemComma => ",",
        _ => null,
    };

    /// <summary>
    /// Copy/paste chords that belong to the app, not the remote shell: Cmd+C/V on macOS, Ctrl+Shift+C/V elsewhere
    /// (plain Ctrl+C must reach the shell as SIGINT).
    /// </summary>
    public static bool IsCopy(Key key, KeyModifiers mods) => key == Key.C && IsClipboardChord(mods);

    public static bool IsPaste(Key key, KeyModifiers mods)
        => (key == Key.V && IsClipboardChord(mods)) || (key == Key.Insert && mods == KeyModifiers.Shift);

    private static bool IsClipboardChord(KeyModifiers mods)
        => OperatingSystem.IsMacOS()
            ? mods == KeyModifiers.Meta
            : mods == (KeyModifiers.Control | KeyModifiers.Shift);
}
