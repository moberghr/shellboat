using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Shellboat.App.ViewModels;

namespace Shellboat.App.Controls;

/// <summary>
/// Drag a connection or folder onto a folder (or empty space = top level) inside the tree, with pointer
/// capture instead of OS drag-and-drop.
/// <para>
/// <b>Why not <c>DragDrop.DoDragDropAsync</c>:</b> on macOS a transfer that holds only an in-process format
/// gives AppKit nothing to put on the pasteboard, and <c>NSDraggingSession</c> throws an Objective-C exception
/// that aborts the process — it cannot be caught from .NET (crash seen 2026-11-05). Moving rows within our own
/// tree never needs the OS, so the drag never leaves the app and behaves the same on every platform.
/// </para>
/// </summary>
public sealed class TreeDragController
{
    private const double Threshold = 4;
    private readonly TreeView _tree;
    private readonly Func<MainWindowViewModel?> _vm;
    private TreeNodeViewModel? _pending;
    private Point _start;
    private bool _dragging;
    private FolderNodeViewModel? _target;

    public TreeDragController(TreeView tree, Func<MainWindowViewModel?> vm)
    {
        _tree = tree;
        _vm = vm;
        tree.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        tree.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
        tree.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
        tree.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => Cancel());
        tree.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_tree).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pending = NodeAt(e.Source);
        _start = e.GetPosition(_tree);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_pending is null || !e.GetCurrentPoint(_tree).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var pos = e.GetPosition(_tree);
        if (!_dragging)
        {
            if (Math.Abs(pos.X - _start.X) <= Threshold && Math.Abs(pos.Y - _start.Y) <= Threshold)
            {
                return;
            }

            _dragging = true;
            e.Pointer.Capture(_tree);
            if (_vm() is { } vm)
            {
                vm.StatusText = $"Moving {_pending.Title} — drop on a folder, or on empty space for the top level. Esc cancels.";
            }
        }

        e.Handled = true;
        _target = InsideTree(pos) ? FolderAt(pos) : null;
        if (_target is not null && _pending is FolderNodeViewModel dragged && _vm()?.Tree.CanMoveFolder(dragged.Id, _target.Id) != true)
        {
            _target = null; // a folder can't go into itself or its own subtree
        }

        _vm()?.Tree.MarkDropTarget(_target);
    }

    private async void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var dragged = _pending;
        var wasDragging = _dragging;
        var pos = e.GetPosition(_tree);
        var target = _target;
        Reset();
        if (!wasDragging || dragged is null || _vm() is not { } vm)
        {
            return;
        }

        e.Handled = true;
        e.Pointer.Capture(null);
        vm.StatusText = "";
        if (!InsideTree(pos))
        {
            return; // let go outside the tree: no move
        }

        if (dragged is FolderNodeViewModel f && target is not null && !vm.Tree.CanMoveFolder(f.Id, target.Id))
        {
            return;
        }

        if (ReferenceEquals(dragged, target))
        {
            return;
        }

        await vm.MoveAsync(dragged, target);
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
        if (!_dragging && _pending is null)
        {
            return;
        }

        var wasDragging = _dragging;
        Reset();
        if (wasDragging && _vm() is { } vm)
        {
            vm.StatusText = "";
        }
    }

    private void Reset()
    {
        _pending = null;
        _dragging = false;
        _target = null;
        _vm()?.Tree.MarkDropTarget(null);
    }

    private bool InsideTree(Point p) => p.X >= 0 && p.Y >= 0 && p.X <= _tree.Bounds.Width && p.Y <= _tree.Bounds.Height;

    /// <summary>The folder under the pointer: the folder row itself, or the folder holding the connection row.</summary>
    private FolderNodeViewModel? FolderAt(Point p)
    {
        var item = (_tree.InputHitTest(p) as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);
        while (item is not null)
        {
            if (item.DataContext is FolderNodeViewModel folder)
            {
                return folder;
            }

            item = item.FindAncestorOfType<TreeViewItem>();
        }

        return null;
    }

    private static TreeNodeViewModel? NodeAt(object? source)
        => (source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext as TreeNodeViewModel;
}
