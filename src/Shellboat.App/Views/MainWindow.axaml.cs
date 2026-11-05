using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Shellboat.App.ViewModels;

namespace Shellboat.App.Views;

/// <summary>
/// Wires tree gestures (double-click/Enter connects, Delete, F2, drag-and-drop into folders) and tab drags
/// (reorder, tear off) to the view model, and keeps one window open per detached session. No connection or
/// protocol logic here (§2.3).
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ConnectionTree.DoubleTapped += OnTreeDoubleTapped;
        ConnectionTree.KeyDown += OnTreeKeyDown;
        _ = new Controls.TreeDragController(ConnectionTree, () => Vm);
        _ = new Controls.TabDragController(SessionTabs, this, () => Vm, DetachAt);
    }

    private readonly Dictionary<SessionTabViewModel, DetachedSessionWindow> _detached = [];
    private MainWindowViewModel? _watched;
    private PixelPoint? _nextDetachPosition;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_watched is not null)
        {
            _watched.DetachedTabs.CollectionChanged -= OnDetachedChanged;
        }

        _watched = Vm;
        if (_watched is not null)
        {
            _watched.DetachedTabs.CollectionChanged += OnDetachedChanged;
        }
    }

    /// <summary>A tab dropped outside the strip: its window opens under the pointer.</summary>
    private void DetachAt(SessionTabViewModel tab, PixelPoint screen)
    {
        _nextDetachPosition = new PixelPoint(screen.X - 60, screen.Y - 20);
        Vm?.DetachTabCommand.Execute(tab);
        _nextDetachPosition = null;
    }

    private void OnDetachedChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Vm is not { } vm)
        {
            return;
        }

        foreach (var gone in _detached.Keys.Where(t => !vm.DetachedTabs.Contains(t)).ToList())
        {
            var window = _detached[gone];
            _detached.Remove(gone);
            window.Released = true;
            window.Close();
        }

        foreach (var tab in vm.DetachedTabs.Where(t => !_detached.ContainsKey(t)).ToList())
        {
            var window = new DetachedSessionWindow(vm, tab);
            if (_nextDetachPosition is { } at)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = at;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            window.Closed += (_, _) => _detached.Remove(tab);
            _detached[tab] = window;
            window.Show();
            window.Activate();
        }
    }

    /// <summary>Detached windows don't outlive the main window (they would keep the app running).</summary>
    protected override void OnClosed(EventArgs e)
    {
        foreach (var window in _detached.Values.ToList())
        {
            window.Close();
        }

        base.OnClosed(e);
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private static TreeNodeViewModel? NodeOf(object? source)
        => (source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext as TreeNodeViewModel;

    private async void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (NodeOf(e.Source) is ConnectionNodeViewModel node && Vm is { } vm)
        {
            e.Handled = true;
            await vm.ConnectCommand.ExecuteAsync(node);
        }
    }

    private async void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm || vm.Tree.Selected is not { } node)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when node is ConnectionNodeViewModel:
                e.Handled = true;
                await vm.ConnectCommand.ExecuteAsync(node);
                break;
            case Key.Delete:
            case Key.Back when OperatingSystem.IsMacOS() && e.KeyModifiers == KeyModifiers.Meta:
                e.Handled = true;
                await vm.DeleteCommand.ExecuteAsync(node);
                break;
            case Key.F2:
                e.Handled = true;
                await vm.EditCommand.ExecuteAsync(node);
                break;
        }
    }
}
