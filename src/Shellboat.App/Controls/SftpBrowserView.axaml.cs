using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Shellboat.App.ViewModels;

namespace Shellboat.App.Controls;

/// <summary>
/// The SFTP tab: this computer on the left, the server on the right, transfers below. Wiring only — double-click /
/// Enter opens a row, rows dragged between the panes (<see cref="PaneDragController"/>) and files dropped from the OS
/// file manager are handed to the view model, which decides what to copy where. (Dropping <i>in</i> from the OS is
/// safe — the §2.6 macOS crash is about starting an in-process OS drag.)
/// </summary>
public sealed partial class SftpBrowserView : UserControl
{
    public SftpBrowserView()
    {
        InitializeComponent();
        var remote = Files;
        var local = LocalFiles;

        remote.DoubleTapped += (_, e) => OpenRemote(RowAt<RemoteFileItemViewModel>(e.Source));
        remote.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && remote.SelectedItems?.Count == 1)
            {
                OpenRemote(remote.SelectedItem as RemoteFileItemViewModel);
                e.Handled = true;
            }
        };
        local.DoubleTapped += (_, e) => OpenLocal(RowAt<LocalFileItemViewModel>(e.Source));
        local.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && local.SelectedItems?.Count == 1)
            {
                OpenLocal(local.SelectedItem as LocalFileItemViewModel);
                e.Handled = true;
            }
        };

        // Left → right = upload, right → left = download.
        _ = new PaneDragController(local, remote, RemotePane,
            row => Vm is { } vm && row is LocalFileItemViewModel item ? vm.Local.Targets(item) : [],
            (items, onto) => _ = Vm?.DropOnRemoteAsync([.. items.OfType<LocalFileItemViewModel>()], onto as RemoteFileItemViewModel),
            Hint);
        _ = new PaneDragController(remote, local, LocalPane,
            row => Vm is { } vm && row is RemoteFileItemViewModel item ? vm.Targets(item) : [],
            (items, onto) => _ = Vm?.DropOnLocalAsync([.. items.OfType<RemoteFileItemViewModel>()], onto as LocalFileItemViewModel),
            Hint);

        // Files and folders from the OS file manager onto the server pane = upload.
        remote.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            var accept = e.DataTransfer.Contains(DataFormat.File) && Vm is { } vm && vm.UploadFilesCommand.CanExecute(null);
            e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
            RemotePane.Classes.Set("dropzone", accept);
            e.Handled = true;
        });
        remote.AddHandler(DragDrop.DragLeaveEvent, (_, _) => RemotePane.Classes.Set("dropzone", false));
        remote.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            RemotePane.Classes.Set("dropzone", false);
            var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];
            if (paths.Count > 0 && Vm is { } vm)
            {
                _ = vm.DropPathsOnRemoteAsync(paths, RowAt<RemoteFileItemViewModel>(e.Source));
            }

            e.Handled = true;
        });
    }

    private SftpTabViewModel? Vm => DataContext as SftpTabViewModel;

    private void Hint(string text)
    {
        if (Vm is { } vm && (text.Length > 0 || vm.Message.StartsWith("Dragging", StringComparison.Ordinal)))
        {
            vm.Message = text;
        }
    }

    private void OpenRemote(RemoteFileItemViewModel? item)
    {
        if (item is not null && Vm is { } vm)
        {
            vm.OpenCommand.Execute(item);
        }
    }

    private void OpenLocal(LocalFileItemViewModel? item)
    {
        if (item is not null && Vm is { } vm)
        {
            vm.Local.OpenCommand.Execute(item);
        }
    }

    private static T? RowAt<T>(object? source)
        where T : class
        => (source as Avalonia.Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as T;
}
