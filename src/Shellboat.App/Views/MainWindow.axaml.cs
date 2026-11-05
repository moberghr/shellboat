using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Shellboat.App.ViewModels;

namespace Shellboat.App.Views;

/// <summary>
/// Wires tree gestures (double-click/Enter connects, Delete, F2, drag-and-drop into folders) to the view
/// model. No connection or protocol logic here (§2.3).
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ConnectionTree.DoubleTapped += OnTreeDoubleTapped;
        ConnectionTree.KeyDown += OnTreeKeyDown;
        _ = new Controls.TreeDragController(ConnectionTree, () => Vm);
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
