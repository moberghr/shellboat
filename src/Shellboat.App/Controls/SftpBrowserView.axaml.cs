using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Shellboat.App.ViewModels;

namespace Shellboat.App.Controls;

/// <summary>The SFTP file list. Wiring only: double-click / Enter on a row hands it to the view model; files and
/// folders dropped from the OS file manager are handed over as local paths for upload. (Dropping <i>in</i> is safe —
/// the §2.6 macOS crash is about starting an in-process drag.)</summary>
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
        files.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) && DataContext is SftpTabViewModel vm && vm.UploadFilesCommand.CanExecute(null)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        });
        files.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];
            if (paths.Count > 0 && DataContext is SftpTabViewModel target)
            {
                _ = target.UploadPathsAsync(paths);
            }

            e.Handled = true;
        });
    }

    private void Open(RemoteFileItemViewModel? item)
    {
        if (item is not null && DataContext is SftpTabViewModel vm)
        {
            vm.OpenCommand.Execute(item);
        }
    }
}
