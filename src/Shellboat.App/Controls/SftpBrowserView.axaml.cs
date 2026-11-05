using Avalonia.Controls;
using Avalonia.Input;
using Shellboat.App.ViewModels;

namespace Shellboat.App.Controls;

/// <summary>The SFTP file list. Wiring only: double-click / Enter on a row hands it to the view model.</summary>
public sealed partial class SftpBrowserView : UserControl
{
    public SftpBrowserView()
    {
        InitializeComponent();
        var files = Files;
        files.DoubleTapped += (_, e) => Open((e.Source as Control)?.DataContext as RemoteFileItemViewModel);
        files.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && files.SelectedItems?.Count == 1)
            {
                Open(files.SelectedItem as RemoteFileItemViewModel);
                e.Handled = true;
            }
        };
    }

    private void Open(RemoteFileItemViewModel? item)
    {
        if (item is not null && DataContext is SftpTabViewModel vm)
        {
            vm.OpenCommand.Execute(item);
        }
    }
}
