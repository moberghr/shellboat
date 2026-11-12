using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Shellboat.App.Controls;

/// <summary>
/// Drag rows from one SFTP pane's list onto the other pane (or onto a folder row in it), with pointer capture
/// instead of OS drag-and-drop — same reason as <see cref="TreeDragController"/>: an in-process-only
/// <c>DoDragDropAsync</c> aborts the app on macOS (§2.6). Wiring only: what a drop means is the view model's call.
/// </summary>
public sealed class PaneDragController
{
    private const double Threshold = 4;
    private readonly ListBox _source;
    private readonly ListBox _targetList;
    private readonly Border _targetPane;
    private readonly Func<object, IReadOnlyList<object>> _itemsFor;
    private readonly Action<IReadOnlyList<object>, object?> _drop;
    private readonly Action<string> _hint;
    private IReadOnlyList<object>? _pending;
    private Point _start;
    private bool _dragging;

    /// <param name="itemsFor">The rows a drag that starts on this row carries (the selection when the row is in it).</param>
    /// <param name="drop">Called with the dragged rows and the row they were dropped on (null = empty space).</param>
    /// <param name="hint">Status-line text while dragging ("" clears it).</param>
    public PaneDragController(ListBox source, ListBox targetList, Border targetPane, Func<object, IReadOnlyList<object>> itemsFor, Action<IReadOnlyList<object>, object?> drop, Action<string> hint)
    {
        _source = source;
        _targetList = targetList;
        _targetPane = targetPane;
        _itemsFor = itemsFor;
        _drop = drop;
        _hint = hint;
        source.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        source.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
        source.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
        source.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => Reset());
        source.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_source);
        if (!point.Properties.IsLeftButtonPressed || e.ClickCount > 1)
        {
            return;
        }

        // Read the selection before the ListBox handles the press (tunnel), so dragging a multi-selection keeps it.
        var row = RowAt(e.Source);
        _pending = row is null ? null : _itemsFor(row);
        _start = e.GetPosition(_source);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_pending is null || !e.GetCurrentPoint(_source).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var pos = e.GetPosition(_source);
        if (!_dragging)
        {
            if (Math.Abs(pos.X - _start.X) <= Threshold && Math.Abs(pos.Y - _start.Y) <= Threshold)
            {
                return;
            }

            _dragging = true;
            e.Pointer.Capture(_source);
            var what = _pending.Count == 1 ? "1 item" : $"{_pending.Count} items";
            _hint($"Dragging {what} — drop on the other pane (or on a folder in it). Esc cancels.");
        }

        e.Handled = true;
        _targetPane.Classes.Set("dropzone", OverTarget(e));
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var items = _pending;
        var wasDragging = _dragging;
        var over = wasDragging && OverTarget(e);
        var onto = over ? RowAt(_targetList.InputHitTest(e.GetPosition(_targetList))) : null;
        Reset();
        if (!wasDragging || items is null)
        {
            return;
        }

        e.Handled = true;
        e.Pointer.Capture(null);
        if (over)
        {
            _drop(items, onto);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dragging && e.Key == Key.Escape)
        {
            e.Handled = true;
            Reset();
        }
    }

    private void Reset()
    {
        var wasDragging = _dragging;
        _pending = null;
        _dragging = false;
        _targetPane.Classes.Set("dropzone", false);
        if (wasDragging)
        {
            _hint("");
        }
    }

    private bool OverTarget(PointerEventArgs e)
    {
        var p = e.GetPosition(_targetPane);
        return p.X >= 0 && p.Y >= 0 && p.X <= _targetPane.Bounds.Width && p.Y <= _targetPane.Bounds.Height;
    }

    private static object? RowAt(object? source)
        => (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext;
}
