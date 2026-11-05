using System.Text;

namespace Shellboat.Terminal.Tests;

public class TerminalKeyEncoderTests
{
    private static string S(byte[] b) => Encoding.ASCII.GetString(b).Replace("\x1b", "ESC");

    [Theory]
    [InlineData(TerminalKey.Up, false, "ESC[A")]
    [InlineData(TerminalKey.Up, true, "ESCOA")]
    [InlineData(TerminalKey.Left, false, "ESC[D")]
    [InlineData(TerminalKey.Home, false, "ESC[H")]
    [InlineData(TerminalKey.Delete, false, "ESC[3~")]
    [InlineData(TerminalKey.PageDown, false, "ESC[6~")]
    [InlineData(TerminalKey.F1, false, "ESCOP")]
    [InlineData(TerminalKey.F5, false, "ESC[15~")]
    [InlineData(TerminalKey.F12, false, "ESC[24~")]
    [InlineData(TerminalKey.Enter, false, "\r")]
    [InlineData(TerminalKey.Backspace, false, "\x7f")]
    public void Unmodified_keys(TerminalKey key, bool appCursor, string expected)
        => Assert.Equal(expected, S(TerminalKeyEncoder.Encode(key, KeyMods.None, appCursor)));

    [Fact]
    public void Modified_cursor_keys_use_the_xterm_parameter_even_in_application_mode()
    {
        Assert.Equal("ESC[1;5C", S(TerminalKeyEncoder.Encode(TerminalKey.Right, KeyMods.Control, applicationCursor: true)));
        Assert.Equal("ESC[1;2A", S(TerminalKeyEncoder.Encode(TerminalKey.Up, KeyMods.Shift, false)));
        Assert.Equal("ESC[3;3~", S(TerminalKeyEncoder.Encode(TerminalKey.Delete, KeyMods.Alt, false)));
    }

    [Fact]
    public void Shift_tab_is_backtab() => Assert.Equal("ESC[Z", S(TerminalKeyEncoder.Encode(TerminalKey.Tab, KeyMods.Shift, false)));

    [Theory]
    [InlineData("c", 0x03)]
    [InlineData("C", 0x03)]
    [InlineData("d", 0x04)]
    [InlineData("[", 0x1B)]
    [InlineData(" ", 0x00)]
    [InlineData("\\", 0x1C)]
    public void Control_chords_become_c0_codes(string ch, byte code)
        => Assert.Equal([code], TerminalKeyEncoder.EncodeText(ch, KeyMods.Control));

    [Fact]
    public void Alt_prefixes_escape_and_text_is_utf8()
    {
        Assert.Equal([0x1B, (byte)'x'], TerminalKeyEncoder.EncodeText("x", KeyMods.Alt));
        Assert.Equal(Encoding.UTF8.GetBytes("čć€"), TerminalKeyEncoder.EncodeText("čć€", KeyMods.None));
    }

    [Fact]
    public void Paste_normalises_newlines_and_brackets_when_asked()
    {
        Assert.Equal("a\rb\rc", Encoding.UTF8.GetString(TerminalKeyEncoder.EncodePaste("a\r\nb\nc", bracketed: false)));
        Assert.Equal("\x1b[200~ls\r\x1b[201~", Encoding.UTF8.GetString(TerminalKeyEncoder.EncodePaste("ls\n", bracketed: true)));
    }

    [Fact]
    public void Bracketed_paste_cannot_be_closed_from_inside_the_pasted_text()
    {
        var evil = "echo hi\x1b[201~rm -rf ~\n";
        var sent = Encoding.UTF8.GetString(TerminalKeyEncoder.EncodePaste(evil, bracketed: true));
        Assert.Equal(1, CountOf(sent, "\x1b[201~"));
        Assert.EndsWith("\x1b[201~", sent);
    }

    private static int CountOf(string s, string needle)
    {
        var n = 0;
        for (var i = s.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = s.IndexOf(needle, i + 1, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }
}
