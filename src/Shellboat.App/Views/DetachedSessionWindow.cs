using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Shellboat.App.Controls;
using Shellboat.App.ViewModels;

namespace Shellboat.App.Views;

/// <summary>
/// One session in its own window: a slim header (icon, title, "Attach to main window") over the same
/// <see cref="SessionHost"/> the tab strip uses. Wiring only — attach/close go through the view model.
/// </summary>
public sealed class DetachedSessionWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly SessionHost _host;

    /// <summary>Set when the view model already let go of the tab (attached or closed), so closing the window
    /// must not close the session again.</summary>
    public bool Released { get; set; }

    public SessionTabViewModel Tab { get; }

    public DetachedSessionWindow(MainWindowViewModel vm, SessionTabViewModel tab)
    {
        _vm = vm;
        Tab = tab;
        DataContext = tab;
        _host = new SessionHost { DataContext = tab };
        Width = 1000;
        Height = 680;
        MinWidth = 420;
        MinHeight = 240;
        Icon = new WindowIcon(new Bitmap(AssetLoader.Open(new Uri("avares://Shellboat.App/Assets/shellboat-256.png"))));
        this[!TitleProperty] = new Binding(nameof(SessionTabViewModel.Title)) { StringFormat = "{0} — Shellboat" };
        Background = Brush("Bg.Window");

        var icon = new PathIcon { Width = 13, Height = 13, VerticalAlignment = VerticalAlignment.Center };
        icon[!PathIcon.DataProperty] = new Binding(nameof(SessionTabViewModel.IconKey)) { Converter = ResourceGeometry.Instance };
        icon[!ForegroundProperty] = new Binding(nameof(SessionTabViewModel.IconBrushKey)) { Converter = ResourceBrush.Instance };
        var title = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        title[!TextBlock.TextProperty] = new Binding(nameof(SessionTabViewModel.Title));
        var strip = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 2) };
        strip[!IsVisibleProperty] = new Binding(nameof(SessionTabViewModel.HasTabColor));
        strip[!Border.BackgroundProperty] = new Binding(nameof(SessionTabViewModel.TabColor)) { Converter = ResourceBrush.Instance };
        var attach = new Button { Content = "Attach to main window", Padding = new Thickness(8, 2), FontSize = 12 };
        attach.Click += (_, _) => _vm.AttachTabCommand.Execute(Tab);
        ToolTip.SetTip(attach, "Put this session back into the main window's tabs");

        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(attach, Dock.Right);
        header.Children.Add(attach);
        header.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { strip, icon, title } });

        var root = new DockPanel();
        var bar = new Border
        {
            Background = Brush("Bg.Chrome"),
            BorderBrush = Brush("Border"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 4),
            Child = header,
        };
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(_host);
        Content = root;
        AddHandler(KeyDownEvent, OnShortcutKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _menu = new MainMenu(_vm, () => Tab, OperatingSystem.IsMacOS(), () => _vm.QuitFromMenu?.Invoke());
        NativeMenu.SetMenu(this, _menu.Menu);
    }

    private readonly MainMenu _menu;

    /// <summary>Same main menu as the main window, acting on this window's session; updated in place.</summary>
    public void RefreshMenu() => _menu.Refresh();

    private async void OnShortcutKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (_vm.Settings.Shortcuts.Match(e.Key, e.KeyModifiers) is { } action)
        {
            e.Handled = true;
            await _vm.RunActionAsync(action, Tab);
        }
    }

    private bool _closeApproved;

    /// <summary>Closing the window ends its session: ask first when it is connected (Settings).</summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (Released || _closeApproved || _vm.QuitApproved || !_vm.NeedsCloseConfirmation([Tab]))
        {
            return;
        }

        e.Cancel = true;
        if (await _vm.ConfirmCloseAsync([Tab]))
        {
            _closeApproved = true;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _host.Release();
        if (!Released)
        {
            _ = _vm.EndSessionAsync(Tab); // the window is gone (confirmed in OnClosing): end the session
        }
    }

    private static IBrush? Brush(string key)
        => Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var v) == true ? v as IBrush : null;
}
