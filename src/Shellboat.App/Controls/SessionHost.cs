using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Shellboat.App.ViewModels;

namespace Shellboat.App.Controls;

/// <summary>
/// The content of a session tab: the terminal or the RDP viewport, with the "connecting / failed — reconnect"
/// card on top. Picks the surface from the tab's type; wiring only.
/// </summary>
public sealed class SessionHost : UserControl
{
    private readonly Grid _root = new();
    private readonly Border _overlay;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 420 };
    private readonly Button _reconnect = new() { Content = "Reconnect", HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Border _bar;
    private readonly TextBlock _barText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly Button _barReconnect = new() { Content = "Reconnect", Padding = new Thickness(8, 2) };
    private SessionTabViewModel? _tab;

    public SessionHost()
    {
        _overlay = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#E6161B21")),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Spacing = 12, Children = { _status, _reconnect } },
        };
        // For terminals the output stays readable after a disconnect, so the reconnect affordance is a slim bar.
        _bar = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#2A323C")),
            Padding = new Thickness(10, 4),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _barText, _barReconnect } },
        };
        _reconnect.Click += async (_, _) => await (_tab?.ConnectAsync() ?? Task.CompletedTask);
        _barReconnect.Click += async (_, _) => await (_tab?.ConnectAsync() ?? Task.CompletedTask);
        Content = _root;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_tab is not null)
        {
            _tab.PropertyChanged -= OnTabChanged;
        }

        _tab = DataContext as SessionTabViewModel;
        _root.Children.Clear();
        switch (_tab)
        {
            case TerminalTabViewModel terminal:
                _root.Children.Add(new TerminalControl { Tab = terminal });
                break;
            case RdpTabViewModel rdp:
                _root.Children.Add(new RdpViewportView { DataContext = rdp });
                break;
        }

        _root.Children.Add(_overlay);
        _root.Children.Add(_bar);
        if (_tab is not null)
        {
            _tab.PropertyChanged += OnTabChanged;
        }

        Refresh();
    }

    private void OnTabChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (_tab is null)
        {
            _overlay.IsVisible = false;
            _bar.IsVisible = false;
            return;
        }

        var isTerminal = _tab is TerminalTabViewModel;
        var showCard = isTerminal ? _tab.IsOverlayVisible : _tab.State != SessionState.Connected;
        _overlay.IsVisible = showCard;
        _status.Text = _tab.StatusText;
        _reconnect.IsVisible = _tab.CanReconnect;
        _bar.IsVisible = isTerminal && !showCard && _tab.CanReconnect;
        _barText.Text = _tab.StatusText;
    }
}
