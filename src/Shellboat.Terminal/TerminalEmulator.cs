using System;
using System.Text;
using XtermSharp;

namespace Shellboat.Terminal;

/// <summary>
/// Thread-safe facade over the vendored XtermSharp engine. Bytes from the remote end arrive on a network
/// thread (<see cref="Feed"/>); the UI takes <see cref="Snapshot"/>s and sends keys. Everything that touches
/// the engine happens under one lock, so a snapshot never sees a half-applied escape sequence.
/// </summary>
public sealed class TerminalEmulator
{
    private readonly object _gate = new();
    private readonly XtermSharp.Terminal _terminal;
    private readonly Delegate _delegate;

    public TerminalEmulator(int cols = 80, int rows = 24)
    {
        _delegate = new Delegate(this);
        // ConvertEol off: a remote PTY already sends CRLF; converting would double-space nothing but would
        // break programs that move the cursor with a bare LF.
        var options = new TerminalOptions { Cols = cols, Rows = rows, ConvertEol = false, TermName = "xterm-256color" };
        _terminal = new XtermSharp.Terminal(_delegate, options);
    }

    /// <summary>Bytes the terminal wants sent to the remote end (keyboard input, query responses).</summary>
    public event Action<byte[]>? Output;

    /// <summary>The screen changed. Raised on whichever thread changed it; the UI should coalesce.</summary>
    public event Action? Changed;

    public event Action<string>? TitleChanged;

    public int Cols
    {
        get
        {
            lock (_gate)
            {
                return _terminal.Cols;
            }
        }
    }

    public int Rows
    {
        get
        {
            lock (_gate)
            {
                return _terminal.Rows;
            }
        }
    }

    public bool ApplicationCursor
    {
        get
        {
            lock (_gate)
            {
                return _terminal.ApplicationCursor;
            }
        }
    }

    public bool BracketedPaste
    {
        get
        {
            lock (_gate)
            {
                return _terminal.BracketedPasteMode;
            }
        }
    }

    public void Feed(byte[] data, int length)
    {
        lock (_gate)
        {
            _terminal.Feed(data, length);
        }

        Changed?.Invoke();
    }

    public void Feed(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Feed(bytes, bytes.Length);
    }

    public void Resize(int cols, int rows)
    {
        if (cols < 2 || rows < 1)
        {
            return;
        }

        lock (_gate)
        {
            if (cols == _terminal.Cols && rows == _terminal.Rows)
            {
                return;
            }

            _terminal.Resize(cols, rows);
        }

        Changed?.Invoke();
    }

    /// <summary>Scroll the view through history. Negative = back in time.</summary>
    public void ScrollView(int lines)
    {
        lock (_gate)
        {
            _terminal.ScrollLines(lines);
        }

        Changed?.Invoke();
    }

    /// <summary>Jump back to the live bottom (typing does this).</summary>
    public void ScrollToBottom()
    {
        bool moved;
        lock (_gate)
        {
            var buffer = _terminal.Buffer;
            moved = buffer.YDisp != buffer.YBase;
            buffer.YDisp = buffer.YBase;
        }

        if (moved)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Send user input to the remote end. Scrolls the view back to live output first.</summary>
    public void SendInput(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        ScrollToBottom();
        Output?.Invoke(bytes);
    }

    public TerminalSnapshot Snapshot()
    {
        lock (_gate)
        {
            var buffer = _terminal.Buffer;
            var cols = _terminal.Cols;
            var rows = _terminal.Rows;
            var cells = new TerminalCell[cols * rows];
            var top = buffer.YDisp;
            for (var row = 0; row < rows; row++)
            {
                var lineIndex = top + row;
                BufferLine? line = lineIndex < buffer.Lines.Length ? buffer.Lines[lineIndex] : null;
                var continuation = false;
                for (var col = 0; col < cols; col++)
                {
                    var cell = line is not null && col < line.Length ? Convert(line[col]) : TerminalCell.Blank;
                    // The engine stores the right half of a wide glyph as an ordinary empty cell; mark it so
                    // the renderer gives the glyph both cells instead of drawing a space over its right half.
                    cells[row * cols + col] = continuation ? cell with { Width = 0 } : cell;
                    continuation = !continuation && cell.Width == 2;
                }
            }

            var scrollOffset = buffer.YBase - buffer.YDisp;
            return new TerminalSnapshot(cols, rows, cells, buffer.X, buffer.Y,
                !_terminal.CursorHidden && scrollOffset == 0, scrollOffset) { ViewportTop = top };
        }
    }

    /// <summary>
    /// Text between two absolute buffer positions (row = <see cref="TerminalSnapshot.ViewportTop"/> + visible
    /// row), inclusive of the end cell. Wrapped lines are joined; hard line ends become '\n'.
    /// </summary>
    public string GetText(int startRow, int startCol, int endRow, int endCol)
    {
        if (endRow < startRow || (endRow == startRow && endCol < startCol))
        {
            (startRow, startCol, endRow, endCol) = (endRow, endCol, startRow, startCol);
        }

        lock (_gate)
        {
            var buffer = _terminal.Buffer;
            var sb = new StringBuilder();
            var last = Math.Min(endRow, buffer.Lines.Length - 1);
            for (var row = Math.Max(0, startRow); row <= last; row++)
            {
                var line = buffer.Lines[row];
                var from = row == startRow ? startCol : 0;
                var to = row == endRow ? Math.Min(endCol + 1, line.Length) : line.Length;
                var text = buffer.TranslateBufferLineToString(row, trimRight: row != endRow, from, to).ToString();
                sb.Append(text);
                var nextWraps = row + 1 < buffer.Lines.Length && buffer.Lines[row + 1].IsWrapped;
                if (row != last && !nextWraps)
                {
                    sb.Append('\n');
                }
            }

            return sb.ToString();
        }
    }

    private static TerminalCell Convert(CharData ch)
    {
        var attr = ch.Attribute;
        var bg = attr & 0x1FF;
        var fg = (attr >> 9) & 0x1FF;
        var style = (CellStyle)((attr >> 18) & 0xFF);
        var text = ch.Code switch
        {
            0 => " ",
            _ when ch.Code is >= 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF) => char.ConvertFromUtf32(ch.Code),
            _ => "�",
        };
        return new TerminalCell(text, PaletteOrDefault(fg), PaletteOrDefault(bg), style, ch.Width);
    }

    private static int PaletteOrDefault(int color) => color < 256 ? color : TerminalCell.DefaultColor;

    private sealed class Delegate(TerminalEmulator owner) : SimpleTerminalDelegate
    {
        public override void Send(byte[] data) => owner.Output?.Invoke(data);

        public override void SetTerminalTitle(XtermSharp.Terminal source, string title) => owner.TitleChanged?.Invoke(title);

        /// <summary>No reading the screen back: a remote host should not be able to pull our buffer.</summary>
        public override bool IsProcessTrusted() => false;
    }
}
