using System;

namespace Shellboat.Terminal;

/// <summary>Non-text keys the terminal encodes. Text keys go through <see cref="TerminalKeyEncoder.EncodeText"/>.</summary>
public enum TerminalKey
{
    Enter,
    Tab,
    Backspace,
    Escape,
    Up,
    Down,
    Left,
    Right,
    Home,
    End,
    PageUp,
    PageDown,
    Insert,
    Delete,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
}

[Flags]
public enum KeyMods
{
    None = 0,
    Shift = 1,
    Alt = 2,
    Control = 4,
}

/// <summary>
/// Turns keys into the byte sequences an xterm sends. Pure, so every mapping is unit-testable without a UI.
/// Modified cursor/function keys use xterm's <c>CSI 1;m X</c> form (m = 1 + shift + 2·alt + 4·ctrl).
/// </summary>
public static class TerminalKeyEncoder
{
    private const byte Esc = 0x1B;

    public static byte[] Encode(TerminalKey key, KeyMods mods, bool applicationCursor)
    {
        var m = ModifierParam(mods);
        switch (key)
        {
            case TerminalKey.Enter:
                return mods.HasFlag(KeyMods.Alt) ? [Esc, 0x0D] : [0x0D];
            case TerminalKey.Tab:
                return mods.HasFlag(KeyMods.Shift) ? Csi("Z") : [0x09];
            case TerminalKey.Backspace:
                // DEL, as every modern terminal sends; Ctrl+Backspace sends BS (^H), Alt prefixes ESC.
                var bs = mods.HasFlag(KeyMods.Control) ? (byte)0x08 : (byte)0x7F;
                return mods.HasFlag(KeyMods.Alt) ? [Esc, bs] : [bs];
            case TerminalKey.Escape:
                return [Esc];
            case TerminalKey.Up:
                return Cursor('A', m, applicationCursor);
            case TerminalKey.Down:
                return Cursor('B', m, applicationCursor);
            case TerminalKey.Right:
                return Cursor('C', m, applicationCursor);
            case TerminalKey.Left:
                return Cursor('D', m, applicationCursor);
            case TerminalKey.Home:
                return Cursor('H', m, applicationCursor);
            case TerminalKey.End:
                return Cursor('F', m, applicationCursor);
            case TerminalKey.Insert:
                return Tilde(2, m);
            case TerminalKey.Delete:
                return Tilde(3, m);
            case TerminalKey.PageUp:
                return Tilde(5, m);
            case TerminalKey.PageDown:
                return Tilde(6, m);
            case TerminalKey.F1:
                return Ss3OrCsi('P', m);
            case TerminalKey.F2:
                return Ss3OrCsi('Q', m);
            case TerminalKey.F3:
                return Ss3OrCsi('R', m);
            case TerminalKey.F4:
                return Ss3OrCsi('S', m);
            case TerminalKey.F5:
                return Tilde(15, m);
            case TerminalKey.F6:
                return Tilde(17, m);
            case TerminalKey.F7:
                return Tilde(18, m);
            case TerminalKey.F8:
                return Tilde(19, m);
            case TerminalKey.F9:
                return Tilde(20, m);
            case TerminalKey.F10:
                return Tilde(21, m);
            case TerminalKey.F11:
                return Tilde(23, m);
            case TerminalKey.F12:
                return Tilde(24, m);
            default:
                return [];
        }
    }

    /// <summary>
    /// Encode typed text. With Control, a single ASCII letter or one of <c>@[\]^_ ?</c> becomes its C0 control
    /// code (Ctrl+C → 0x03, Ctrl+Space → NUL); Alt prefixes ESC (meta-sends-escape).
    /// </summary>
    public static byte[] EncodeText(string text, KeyMods mods)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        byte[] body;
        if (mods.HasFlag(KeyMods.Control) && text.Length == 1 && TryControlCode(text[0], out var code))
        {
            body = [code];
        }
        else
        {
            body = System.Text.Encoding.UTF8.GetBytes(text);
        }

        if (!mods.HasFlag(KeyMods.Alt))
        {
            return body;
        }

        var withEsc = new byte[body.Length + 1];
        withEsc[0] = Esc;
        body.CopyTo(withEsc, 1);
        return withEsc;
    }

    /// <summary>Wrap a paste in bracketed-paste markers when the application asked for them (DECSET 2004).</summary>
    public static byte[] EncodePaste(string text, bool bracketed)
    {
        // Normalise line endings to CR, which is what Enter sends.
        var normalized = text.Replace("\r\n", "\r").Replace('\n', '\r');
        if (bracketed)
        {
            // A paste must not be able to close the bracket itself and smuggle in keystrokes.
            normalized = "\x1b[200~" + normalized.Replace("\x1b[201~", "") + "\x1b[201~";
        }

        return System.Text.Encoding.UTF8.GetBytes(normalized);
    }

    internal static bool TryControlCode(char c, out byte code)
    {
        var upper = char.ToUpperInvariant(c);
        if (upper is >= '@' and <= '_')
        {
            code = (byte)(upper - '@');
            return true;
        }

        switch (c)
        {
            case ' ':
            case '2':
                code = 0x00;
                return true;
            case '/':
            case '7':
                code = 0x1F;
                return true;
            case '?':
            case '8':
                code = 0x7F;
                return true;
            default:
                code = 0;
                return false;
        }
    }

    private static int ModifierParam(KeyMods mods)
    {
        var m = 1;
        if (mods.HasFlag(KeyMods.Shift))
        {
            m += 1;
        }

        if (mods.HasFlag(KeyMods.Alt))
        {
            m += 2;
        }

        if (mods.HasFlag(KeyMods.Control))
        {
            m += 4;
        }

        return m;
    }

    private static byte[] Cursor(char final, int m, bool applicationCursor)
    {
        if (m > 1)
        {
            return Csi($"1;{m}{final}");
        }

        return applicationCursor ? [Esc, (byte)'O', (byte)final] : [Esc, (byte)'[', (byte)final];
    }

    private static byte[] Ss3OrCsi(char final, int m)
        => m > 1 ? Csi($"1;{m}{final}") : [Esc, (byte)'O', (byte)final];

    private static byte[] Tilde(int number, int m)
        => m > 1 ? Csi($"{number};{m}~") : Csi($"{number}~");

    private static byte[] Csi(string tail)
    {
        var bytes = new byte[tail.Length + 2];
        bytes[0] = Esc;
        bytes[1] = (byte)'[';
        for (var i = 0; i < tail.Length; i++)
        {
            bytes[i + 2] = (byte)tail[i];
        }

        return bytes;
    }
}
