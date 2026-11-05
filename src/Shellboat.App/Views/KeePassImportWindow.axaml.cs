using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Shellboat.App.ViewModels;

namespace Shellboat.App.Views;

public partial class KeePassImportWindow : Window
{
    public KeePassImportWindow()
    {
        InitializeComponent();
    }

    private KeePassImportViewModel? Vm => DataContext as KeePassImportViewModel;

    private void OnImport(object? sender, RoutedEventArgs e)
    {
        Vm?.Accept();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private async void OnBrowseDatabase(object? sender, RoutedEventArgs e)
    {
        if (await PickAsync("Choose KeePass database", [new FilePickerFileType("KeePass database") { Patterns = ["*.kdbx"] }, FilePickerFileTypes.All]) is { } path && Vm is { } vm)
        {
            vm.DatabasePath = path;
        }
    }

    private async void OnBrowseKeyFile(object? sender, RoutedEventArgs e)
    {
        if (await PickAsync("Choose key file", [FilePickerFileTypes.All]) is { } path && Vm is { } vm)
        {
            vm.KeyFilePath = path;
        }
    }

    private async Task<string?> PickAsync(string title, IReadOnlyList<FilePickerFileType> types)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false, FileTypeFilter = types });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
