using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Shellboat.App.ViewModels;

namespace Shellboat.App.Views;

/// <summary>Settings dialog. Code-behind only forwards key presses while a shortcut row is recording, and lists the
/// installed fonts for the font box.</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
    }

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Vm is { } vm && vm.FontSuggestions.Count == 0)
        {
            foreach (var name in FontManager.Current.SystemFonts.Select(f => f.Name).Distinct().Order())
            {
                vm.FontSuggestions.Add(name);
            }
        }
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (Vm?.HandleKey(e.Key, e.KeyModifiers) == true)
        {
            e.Handled = true;
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
