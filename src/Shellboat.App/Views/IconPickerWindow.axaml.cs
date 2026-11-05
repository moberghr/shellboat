using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Shellboat.App.Views;

public partial class IconPickerWindow : Window
{
    public IconPickerWindow()
    {
        InitializeComponent();
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
