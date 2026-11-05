using System;

namespace Shellboat.Terminal;

[Flags]
public enum CellStyle
{
    None = 0,
    Bold = 1,
    Underline = 2,
    Blink = 4,
    Inverse = 8,
    Invisible = 16,
    Dim = 32,
    Italic = 64,
    CrossedOut = 128,
}

/// <summary>
/// One rendered cell. <see cref="Foreground"/>/<see cref="Background"/> are xterm palette indexes 0–255, or
/// <see cref="DefaultColor"/> for the theme's default. <see cref="Width"/> is 0 for the trailing half of a
/// wide (CJK/emoji) character, which the renderer skips.
/// </summary>
public readonly record struct TerminalCell(string Text, int Foreground, int Background, CellStyle Style, int Width)
{
    public const int DefaultColor = -1;

    public static TerminalCell Blank { get; } = new(" ", DefaultColor, DefaultColor, CellStyle.None, 1);
}

/// <summary>An immutable copy of the visible screen, taken under the emulator's lock and rendered off it.</summary>
public sealed class TerminalSnapshot
{
    public TerminalSnapshot(int cols, int rows, TerminalCell[] cells, int cursorCol, int cursorRow, bool cursorVisible, int scrollOffset)
    {
        Cols = cols;
        Rows = rows;
        Cells = cells;
        CursorCol = cursorCol;
        CursorRow = cursorRow;
        CursorVisible = cursorVisible;
        ScrollOffset = scrollOffset;
    }

    public int Cols { get; }
    public int Rows { get; }

    /// <summary>Row-major, <see cref="Cols"/> × <see cref="Rows"/>.</summary>
    public TerminalCell[] Cells { get; }

    public int CursorCol { get; }
    public int CursorRow { get; }

    /// <summary>False when the application hid the cursor or the view is scrolled back into history.</summary>
    public bool CursorVisible { get; }

    /// <summary>How many lines the view is scrolled back from the live bottom; 0 = following output.</summary>
    public int ScrollOffset { get; }

    /// <summary>Absolute buffer line shown on visible row 0 — the anchor for selection coordinates.</summary>
    public int ViewportTop { get; init; }

    public TerminalCell this[int col, int row] => Cells[row * Cols + col];
}
