using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Shellboat.App.ViewModels;

namespace Shellboat.App.Controls;

/// <summary>
/// Drag a tab header sideways to reorder (live), or pull it away from the strip and let go to detach it into its
/// own window. Pointer capture, not OS drag-and-drop — see <see cref="TreeDragController"/> for why (§2.6).
/// Capture sits on the TabControl, not the header, because reordering recreates header containers.
/// </summary>
public sealed class TabDragController
{
    private const double Threshold = 6;
    private readonly TabControl _tabs;
    private readonly Window _window;
    private readonly Func<MainWindowViewModel?> _vm;
    private readonly Action<SessionTabViewModel, PixelPoint> _detach;
    private SessionTabViewModel? _pending;
    private Point _start;
    private bool _dragging;
    private Cursor? _cursor;

    public TabDragController(TabControl tabs, Window window, Func<MainWindowViewModel?> vm, Action<SessionTabViewModel, PixelPoint> detach)
    {
        _tabs = tabs;
        _window = window;
        _vm = vm;
        _detach = detach;
        tabs.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        tabs.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
        tabs.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
        tabs.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => Cancel());
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_tabs).Properties.IsLeftButtonPressed || e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return; // the header's ✕ button keeps its click
        }

        _pending = (e.Source as Visual)?.FindAncestorOfType<TabItem>(includeSelf: true)?.DataContext as SessionTabViewModel;
        _start = e.GetPosition(_tabs);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_pending is null || !e.GetCurrentPoint(_tabs).Properties.IsLeftButtonPressed || _vm() is not { } vm)
        {
            return;
        }

        var pos = e.GetPosition(_tabs);
        if (!_dragging)
        {
            if (Math.Abs(pos.X - _start.X) <= Threshold && Math.Abs(pos.Y - _start.Y) <= Threshold)
            {
                return;
            }

            _dragging = true;
            _cursor = _tabs.Cursor;
            e.Pointer.Capture(_tabs);
        }

        e.Handled = true;
        var tearOff = TabDragGeometry.IsTearOff(StripBounds(), _window.Bounds.Size, e.GetPosition(_window));
        _tabs.Cursor = new Cursor(tearOff ? StandardCursorType.DragCopy : StandardCursorType.DragMove);
        vm.StatusText = tearOff
            ? $"Let go to open \"{_pending.Title}\" in its own window. Esc cancels."
            : $"Moving \"{_pending.Title}\" — pull it away from the tabs to detach. Esc cancels.";
        if (!tearOff)
        {
            vm.MoveTab(_pending, TabDragGeometry.TargetIndex(HeaderBounds(), pos));
        }
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var dragged = _pending;
        var wasDragging = _dragging;
        var inWindow = e.GetPosition(_window);
        Reset();
        if (!wasDragging || dragged is null)
        {
            return;
        }

        e.Handled = true;
        e.Pointer.Capture(null);
        if (TabDragGeometry.IsTearOff(StripBounds(), _window.Bounds.Size, inWindow))
        {
            _detach(dragged, _window.PointToScreen(inWindow));
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dragging && e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel();
        }
    }

    private void Cancel()
    {
        if (_pending is null && !_dragging)
        {
            return;
        }

        Reset();
    }

    private void Reset()
    {
        if (_dragging)
        {
            _tabs.Cursor = _cursor;
            if (_vm() is { } vm)
            {
                vm.StatusText = "";
            }
        }

        _pending = null;
        _dragging = false;
    }

    private IEnumerable<TabItem> Headers()
        => Enumerable.Range(0, _tabs.ItemCount).Select(_tabs.ContainerFromIndex).OfType<TabItem>();

    /// <summary>Header rectangles in TabControl coordinates, in item order.</summary>
    private List<Rect> HeaderBounds()
        => Headers().Select(h => h.TranslatePoint(default, _tabs) is { } p ? new Rect(p, h.Bounds.Size) : default).ToList();

    /// <summary>The tab strip's area in window coordinates (union of the headers).</summary>
    private Rect StripBounds()
    {
        var rects = Headers().Select(h => h.TranslatePoint(default, _window) is { } p ? new Rect(p, h.Bounds.Size) : default).ToList();
        return rects.Count == 0 ? default : rects.Aggregate((a, b) => a.Union(b));
    }
}
