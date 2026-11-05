using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Shellboat.App.Services;
using Shellboat.App.Terminal;
using Shellboat.App.ViewModels;
using Shellboat.Terminal;

namespace Shellboat.App.Controls;

/// <summary>
/// Renders a <see cref="TerminalEmulator"/> as a monospace cell grid and turns keyboard/mouse into terminal
/// input. Rendering reads an immutable <see cref="TerminalSnapshot"/>, so the network thread can keep feeding
/// the emulator while we draw. Visual only — session logic lives in <see cref="TerminalTabViewModel"/>.
/// </summary>
public sealed class TerminalControl : Control
{
    public static readonly StyledProperty<TerminalTabViewModel?> TabProperty =
        AvaloniaProperty.Register<TerminalControl, TerminalTabViewModel?>(nameof(Tab));

    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<TerminalControl, double>(nameof(FontSize), 13);

    private const int WheelLines = 3;
    private readonly Dictionary<uint, IImmutableSolidColorBrush> _brushes = [];
    private Typeface _regular;
    private Typeface _bold;
    private Size _cell = new(8, 16);
    private double _baseline;
    private int _redrawQueued;
    private TerminalEmulator? _emulator;
    private TerminalSnapshot? _lastSnapshot;
    private (int Row, int Col)? _selAnchor;
    private (int Row, int Col)? _selEnd;
    private const string FallbackFonts = "JetBrains Mono,Cascadia Mono,Cascadia Code,Menlo,Consolas,DejaVu Sans Mono,monospace";
    private TerminalTheme _theme = TerminalTheme.Dark;
    private Color _background;
    private Color _foreground;
    private Color _cursorColor;
    private Color _selectionColor;
    private SettingsService? _settings;

    public TerminalControl()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        ApplyTheme();
        UpdateFont();
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
    }

    public TerminalTabViewModel? Tab
    {
        get => GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TabProperty)
        {
            if (_emulator is not null)
            {
                _emulator.Changed -= OnEmulatorChanged;
            }

            _emulator = Tab?.Emulator;
            if (_emulator is not null)
            {
                _emulator.Changed += OnEmulatorChanged;
            }

            if (_settings is not null)
            {
                _settings.PropertyChanged -= OnSettingsChanged;
            }

            _settings = Tab?.Settings;
            if (_settings is not null)
            {
                _settings.PropertyChanged += OnSettingsChanged;
                FontSize = _settings.Current.TerminalFontSize;
            }

            ApplyTheme();
            UpdateFont();

            ClearSelection();
            ResizeGrid(Bounds.Size);
            InvalidateVisual();
        }
        else if (change.Property == FontSizeProperty)
        {
            UpdateFont();
            ResizeGrid(Bounds.Size);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyTheme();
        UpdateFont();
        Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Input);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ResizeGrid(e.NewSize);
    }

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_settings is null || e.PropertyName != nameof(SettingsService.Current))
        {
            return;
        }

        FontSize = _settings.Current.TerminalFontSize; // re-measures via OnPropertyChanged when it changed
        ApplyTheme();
        UpdateFont();
        ResizeGrid(Bounds.Size);
        InvalidateVisual();
    }

    /// <summary>Colours from Settings → Terminal colours (app theme or forced light/dark) and the optional custom
    /// background.</summary>
    private void ApplyTheme()
    {
        var mode = _settings?.Current.TerminalColors ?? Core.Settings.TerminalColorMode.FollowTheme;
        _theme = TerminalTheme.Resolve(mode, ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light, _settings?.Current.TerminalBackground);
        _background = FromRgb(_theme.Background);
        _foreground = FromRgb(_theme.Foreground);
        _cursorColor = FromRgb(_theme.Cursor);
        _selectionColor = Color.FromUInt32(_theme.Selection);
        InvalidateVisual();
    }

    private void UpdateFont()
    {
        var custom = _settings?.Current.TerminalFontFamily;
        var family = new FontFamily(custom is null ? FallbackFonts : $"{custom},{FallbackFonts}");
        _regular = new Typeface(family);
        _bold = new Typeface(family, FontStyle.Normal, FontWeight.Bold);
        var probe = new FormattedText("MMMMMMMMMM", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _regular, FontSize, Brushes.White);
        _cell = new Size(Math.Max(1, probe.WidthIncludingTrailingWhitespace / 10), Math.Ceiling(probe.Height));
        _baseline = probe.Baseline;
    }

    private void ResizeGrid(Size size)
    {
        if (Tab is null || size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var cols = Math.Max(2, (int)Math.Floor((size.Width - 4) / _cell.Width));
        var rows = Math.Max(1, (int)Math.Floor((size.Height - 4) / _cell.Height));
        Tab.Resize(cols, rows);
    }

    private void OnEmulatorChanged()
    {
        // Coalesce: a burst of output (cat of a big file) becomes one repaint per frame.
        if (Interlocked.Exchange(ref _redrawQueued, 1) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref _redrawQueued, 0);
                InvalidateVisual();
            }, DispatcherPriority.Render);
        }
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brush(_background), new Rect(Bounds.Size));
        var emulator = _emulator;
        if (emulator is null)
        {
            return;
        }

        var snap = emulator.Snapshot();
        _lastSnapshot = snap;
        var origin = new Point(2, 2);
        for (var row = 0; row < snap.Rows; row++)
        {
            var y = origin.Y + row * _cell.Height;
            var col = 0;
            while (col < snap.Cols)
            {
                var start = col;
                var first = snap[col, row];
                var (fg, bg) = Colors(first);
                var text = new System.Text.StringBuilder();
                while (col < snap.Cols)
                {
                    var cell = snap[col, row];
                    if (cell.Width == 0)
                    {
                        col++;
                        continue; // trailing half of a wide glyph
                    }

                    if (Colors(cell) != (fg, bg) || cell.Style != first.Style)
                    {
                        break;
                    }

                    text.Append(cell.Style.HasFlag(CellStyle.Invisible) ? " " : cell.Text);
                    col++;
                }

                var rect = new Rect(origin.X + start * _cell.Width, y, (col - start) * _cell.Width, _cell.Height);
                if (bg != _background)
                {
                    context.FillRectangle(Brush(bg), rect);
                }

                var str = text.ToString();
                if (str.Trim().Length > 0)
                {
                    DrawRun(context, str, first.Style, fg, rect.TopLeft, col - start);
                }
            }
        }

        DrawSelection(context, snap, origin);
        DrawCursor(context, snap, origin);
    }

    private void DrawRun(DrawingContext context, string text, CellStyle style, Color fg, Point topLeft, int cells)
    {
        var typeface = style.HasFlag(CellStyle.Bold) ? _bold : _regular;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, FontSize, Brush(fg));
        if (style.HasFlag(CellStyle.Underline))
        {
            ft.SetTextDecorations(TextDecorations.Underline);
        }

        if (style.HasFlag(CellStyle.CrossedOut))
        {
            ft.SetTextDecorations(TextDecorations.Strikethrough);
        }

        // A glyph wider than its cells (fallback font) is squeezed back onto the grid so columns stay aligned.
        var width = cells * _cell.Width;
        if (ft.WidthIncludingTrailingWhitespace > width + 0.5)
        {
            using (context.PushTransform(Matrix.CreateScale(width / ft.WidthIncludingTrailingWhitespace, 1) * Matrix.CreateTranslation(topLeft.X, topLeft.Y)))
            {
                context.DrawText(ft, default);
            }

            return;
        }

        context.DrawText(ft, topLeft);
    }

    private (Color Fg, Color Bg) Colors(TerminalCell cell)
    {
        var fg = cell.Foreground == TerminalCell.DefaultColor ? _foreground : FromRgb(_theme.Rgb(
            cell.Style.HasFlag(CellStyle.Bold) && cell.Foreground < 8 ? cell.Foreground + 8 : cell.Foreground));
        var bg = cell.Background == TerminalCell.DefaultColor ? _background : FromRgb(_theme.Rgb(cell.Background));
        if (cell.Style.HasFlag(CellStyle.Inverse))
        {
            (fg, bg) = (bg, fg);
        }

        if (cell.Style.HasFlag(CellStyle.Dim))
        {
            fg = Color.FromArgb(0xFF, (byte)((fg.R + bg.R) / 2), (byte)((fg.G + bg.G) / 2), (byte)((fg.B + bg.B) / 2));
        }

        return (fg, bg);
    }

    private void DrawCursor(DrawingContext context, TerminalSnapshot snap, Point origin)
    {
        if (!snap.CursorVisible || snap.CursorCol >= snap.Cols || snap.CursorRow >= snap.Rows)
        {
            return;
        }

        var rect = new Rect(origin.X + snap.CursorCol * _cell.Width, origin.Y + snap.CursorRow * _cell.Height, _cell.Width, _cell.Height);
        if (IsFocused)
        {
            context.FillRectangle(Brush(_cursorColor), rect);
            var cell = snap[snap.CursorCol, snap.CursorRow];
            if (cell.Text.Trim().Length > 0)
            {
                DrawRun(context, cell.Text, cell.Style, _background, rect.TopLeft, 1);
            }
        }
        else
        {
            context.DrawRectangle(new Pen(Brush(_cursorColor)), rect.Deflate(0.5));
        }
    }

    private void DrawSelection(DrawingContext context, TerminalSnapshot snap, Point origin)
    {
        if (Selection() is not var (start, end))
        {
            return;
        }

        var brush = Brush(_selectionColor);
        for (var abs = start.Row; abs <= end.Row; abs++)
        {
            var row = abs - snap.ViewportTop;
            if (row < 0 || row >= snap.Rows)
            {
                continue;
            }

            var from = abs == start.Row ? start.Col : 0;
            var to = abs == end.Row ? end.Col : snap.Cols - 1;
            context.FillRectangle(brush, new Rect(origin.X + from * _cell.Width, origin.Y + row * _cell.Height, (to - from + 1) * _cell.Width, _cell.Height));
        }
    }

    // ---- input ---------------------------------------------------------------------------------------

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var emulator = _emulator;
        if (emulator is null || e.Handled)
        {
            return;
        }

        if (AvaloniaKeyMap.IsCopy(e.Key, e.KeyModifiers))
        {
            e.Handled = true;
            await CopySelectionAsync();
            return;
        }

        if (AvaloniaKeyMap.IsPaste(e.Key, e.KeyModifiers))
        {
            e.Handled = true;
            await PasteAsync();
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Shift && e.Key is Key.PageUp or Key.PageDown)
        {
            e.Handled = true;
            emulator.ScrollView(e.Key == Key.PageUp ? -emulator.Rows + 1 : emulator.Rows - 1);
            return;
        }

        var mods = AvaloniaKeyMap.ToMods(e.KeyModifiers);
        if (AvaloniaKeyMap.ToTerminalKey(e.Key) is { } key)
        {
            e.Handled = true;
            ClearSelection();
            emulator.SendInput(TerminalKeyEncoder.Encode(key, mods, emulator.ApplicationCursor));
            return;
        }

        // Ctrl/Alt chords never produce TextInput reliably, so encode them from the physical key.
        // Alt alone is left to TextInput on macOS, where Option composes characters (Option+2 = €, etc.).
        var isChord = mods.HasFlag(KeyMods.Control) || (mods.HasFlag(KeyMods.Alt) && !OperatingSystem.IsMacOS());
        if (isChord && AvaloniaKeyMap.ChordCharacter(e.Key) is { } ch)
        {
            e.Handled = true;
            ClearSelection();
            emulator.SendInput(TerminalKeyEncoder.EncodeText(ch, mods & ~KeyMods.Shift));
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        // Control characters (Enter, Tab, Backspace) were already encoded in OnKeyDown; some platforms also
        // raise them as text, which would send them twice.
        if (_emulator is null || string.IsNullOrEmpty(e.Text) || e.Handled || e.Text.Any(c => c < 0x20 || c == 0x7F))
        {
            return;
        }

        e.Handled = true;
        ClearSelection();
        _emulator.SendInput(TerminalKeyEncoder.EncodeText(e.Text, KeyMods.None));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            _selAnchor = CellAt(point.Position);
            _selEnd = null;
            e.Pointer.Capture(this);
            InvalidateVisual();
        }
        else if (point.Properties.IsRightButtonPressed && _settings?.Current.RightClickPaste != false)
        {
            // Right click pastes, like PuTTY / Ásbrú.
            _ = PasteAsync();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_selAnchor is not null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _selEnd = CellAt(e.GetPosition(this));
            InvalidateVisual();
        }
    }

    protected override async void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        if (Selection() is not null && _settings?.Current.CopyOnSelect != false)
        {
            // Copy-on-select, the convention every Linux terminal user (and Ásbrú) expects.
            await CopySelectionAsync();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_emulator is null)
        {
            return;
        }

        _emulator.ScrollView(-(int)Math.Round(e.Delta.Y * WheelLines));
        e.Handled = true;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    private (int Row, int Col) CellAt(Point p)
    {
        var snap = _lastSnapshot;
        var cols = snap?.Cols ?? 80;
        var rows = snap?.Rows ?? 24;
        var col = Math.Clamp((int)((p.X - 2) / _cell.Width), 0, cols - 1);
        var row = Math.Clamp((int)((p.Y - 2) / _cell.Height), 0, rows - 1);
        return ((snap?.ViewportTop ?? 0) + row, col);
    }

    private ((int Row, int Col) Start, (int Row, int Col) End)? Selection()
    {
        if (_selAnchor is not { } a || _selEnd is not { } b || a == b)
        {
            return null;
        }

        return (a.Row < b.Row || (a.Row == b.Row && a.Col <= b.Col)) ? (a, b) : (b, a);
    }

    private void ClearSelection()
    {
        if (_selAnchor is null && _selEnd is null)
        {
            return;
        }

        _selAnchor = null;
        _selEnd = null;
        InvalidateVisual();
    }

    private async Task CopySelectionAsync()
    {
        if (_emulator is null || Selection() is not var (start, end) || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        var text = _emulator.GetText(start.Row, start.Col, end.Row, end.Col);
        if (text.Length > 0)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private async Task PasteAsync()
    {
        if (_emulator is null || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        var text = await clipboard.TryGetTextAsync();
        if (!string.IsNullOrEmpty(text))
        {
            _emulator.SendInput(TerminalKeyEncoder.EncodePaste(text, _emulator.BracketedPaste));
        }
    }

    private IImmutableSolidColorBrush Brush(Color color)
    {
        var key = color.ToUInt32();
        if (!_brushes.TryGetValue(key, out var brush))
        {
            brush = new ImmutableSolidColorBrush(color);
            _brushes[key] = brush;
        }

        return brush;
    }

    private static Color FromRgb(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
