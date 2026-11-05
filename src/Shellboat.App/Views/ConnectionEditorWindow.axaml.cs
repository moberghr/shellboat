using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Shellboat.App.ViewModels;

namespace Shellboat.App.Views;

public partial class ConnectionEditorWindow : Window
{
    public ConnectionEditorWindow()
    {
        InitializeComponent();
        Opened += (_, _) => HostBox.Focus();
    }

    private ConnectionEditorViewModel? Vm => DataContext as ConnectionEditorViewModel;

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (Vm?.TryBuild() is not null)
        {
            Close(true);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private async void OnBrowseKey(object? sender, RoutedEventArgs e)
    {
        var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        var start = Directory.Exists(sshDir) ? await StorageProvider.TryGetFolderFromPathAsync(sshDir) : null;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose private key",
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path && Vm is { } vm)
        {
            vm.PrivateKeyPath = path;
        }
    }
}
