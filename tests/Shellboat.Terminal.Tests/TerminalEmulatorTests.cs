using System.Text;

namespace Shellboat.Terminal.Tests;

public class TerminalEmulatorTests
{
    private static string Row(TerminalSnapshot s, int row)
    {
        var sb = new StringBuilder();
        for (var c = 0; c < s.Cols; c++)
        {
            sb.Append(s[c, row].Text);
        }

        return sb.ToString().TrimEnd();
    }

    [Fact]
    public void Plain_text_and_crlf_land_on_rows()
    {
        var t = new TerminalEmulator(20, 5);
        t.Feed("hello\r\nworld");
        var s = t.Snapshot();
        Assert.Equal("hello", Row(s, 0));
        Assert.Equal("world", Row(s, 1));
        Assert.Equal((5, 1), (s.CursorCol, s.CursorRow));
    }

    [Fact]
    public void Bare_lf_does_not_return_the_carriage()
    {
        var t = new TerminalEmulator(20, 5);
        t.Feed("ab\ncd");
        Assert.Equal("  cd", Row(t.Snapshot(), 1));
    }

    [Fact]
    public void Sgr_colours_and_styles_map_to_cells()
    {
        var t = new TerminalEmulator(20, 5);
        t.Feed("\x1b[1;31mR\x1b[0m\x1b[38;5;200;48;5;17mX\x1b[7mI\x1b[0m");
        var s = t.Snapshot();
        Assert.Equal(1, s[0, 0].Foreground);
        Assert.True(s[0, 0].Style.HasFlag(CellStyle.Bold));
        Assert.Equal(200, s[1, 0].Foreground);
        Assert.Equal(17, s[1, 0].Background);
        Assert.True(s[2, 0].Style.HasFlag(CellStyle.Inverse));
        Assert.Equal(TerminalCell.DefaultColor, s[3, 0].Foreground);
    }

    [Fact]
    public void Cursor_positioning_and_erase()
    {
        var t = new TerminalEmulator(20, 5);
        t.Feed("xxxxxxxx\x1b[1;3HAB\x1b[K");
        Assert.Equal("xxAB", Row(t.Snapshot(), 0));
    }

    [Fact]
    public void Alternate_screen_restores_the_main_screen_on_exit()
    {
        var t = new TerminalEmulator(20, 5);
        t.Feed("prompt$ ");
        t.Feed("\x1b[?1049h\x1b[2J\x1b[Hvim stuff");
        Assert.Equal("vim stuff", Row(t.Snapshot(), 0));
        t.Feed("\x1b[?1049l");
        Assert.Equal("prompt$", Row(t.Snapshot(), 0));
    }

    [Fact]
    public void Resize_changes_the_grid()
    {
        var t = new TerminalEmulator(80, 24);
        t.Resize(120, 40);
        var s = t.Snapshot();
        Assert.Equal((120, 40), (s.Cols, s.Rows));
    }

    [Fact]
    public void Scrolling_back_hides_the_cursor_and_typing_returns_to_bottom()
    {
        var t = new TerminalEmulator(10, 3);
        for (var i = 0; i < 10; i++)
        {
            t.Feed($"line{i}\r\n");
        }

        t.ScrollView(-2);
        var s = t.Snapshot();
        Assert.Equal(2, s.ScrollOffset);
        Assert.False(s.CursorVisible);

        var sent = new List<byte[]>();
        t.Output += sent.Add;
        t.SendInput([(byte)'a']);
        Assert.Equal(0, t.Snapshot().ScrollOffset);
        Assert.Single(sent);
    }

    [Fact]
    public void GetText_spans_rows_and_joins_with_newlines()
    {
        var t = new TerminalEmulator(20, 5);
        t.Feed("first line\r\nsecond line");
        var top = t.Snapshot().ViewportTop;
        Assert.Equal("line\nsecond", t.GetText(top, 6, top + 1, 5));
    }

    [Fact]
    public void Device_attribute_query_is_answered_through_output()
    {
        var t = new TerminalEmulator(20, 5);
        var sent = new List<byte[]>();
        t.Output += sent.Add;
        t.Feed("\x1b[c");
        Assert.NotEmpty(sent);
        Assert.StartsWith("\x1b[?", Encoding.ASCII.GetString(sent[0]));
    }

    [Fact]
    public void Wide_characters_occupy_two_cells()
    {
        var t = new TerminalEmulator(20, 5);
        t.Feed("中a");
        var s = t.Snapshot();
        Assert.Equal("中", s[0, 0].Text);
        Assert.Equal(2, s[0, 0].Width);
        Assert.Equal(0, s[1, 0].Width);
        Assert.Equal("a", s[2, 0].Text);
    }

    /// <summary>Pins the vendored Terminal.SaveCursor fix (NOTICE.md): ESC 7 / ESC 8 (prompts do this) must not
    /// switch auto-wrap off, or a long line (cat of an SSH key) runs off the right edge instead of wrapping.</summary>
    [Fact]
    public void Long_lines_still_wrap_after_save_and_restore_cursor()
    {
        var t = new TerminalEmulator(10, 5);
        t.Feed("\u001b7\u001b8abcdefghijKLM");
        var s = t.Snapshot();
        Assert.Equal("j", s[9, 0].Text);
        Assert.Equal("K", s[0, 1].Text);
        Assert.Equal("M", s[2, 1].Text);
    }

    [Fact]
    public void Palette_has_the_xterm_cube_and_greys()
    {
        Assert.Equal(0x000000u, TerminalPalette.Rgb(16));
        Assert.Equal(0xFFFFFFu, TerminalPalette.Rgb(231));
        Assert.Equal(0x080808u, TerminalPalette.Rgb(232));
        Assert.Equal(0xEEEEEEu, TerminalPalette.Rgb(255));
    }

    /// <summary>Pins the vendored TerminalOptions.Scrollback setter (NOTICE.md): history is capped at the option.</summary>
    [Theory]
    [InlineData(150)]
    [InlineData(2000)]
    public void Scrollback_length_follows_the_option(int scrollback)
    {
        var t = new TerminalEmulator(20, 5, scrollback);
        for (var i = 0; i < 3000; i++)
        {
            t.Feed($"line {i}\r\n");
        }

        t.ScrollView(-100_000);

        Assert.Equal(scrollback, t.Snapshot().ScrollOffset);
    }
}
