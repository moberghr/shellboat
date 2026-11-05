using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shellboat.Core.Connections;

namespace Shellboat.App.ViewModels;

public enum SessionState
{
    Connecting,
    Connected,
    Disconnected,
    Failed,
}

/// <summary>One entry of a tab's "Tab color" submenu; <see cref="Hex"/> null = no colour.</summary>
public sealed record TabColorOption(string Label, string? Hex, IRelayCommand Apply)
{
    public string SwatchBrushKey => Hex ?? "Text.Muted";
}

/// <summary>One tab on the right: an SSH terminal or an RDP desktop.</summary>
public abstract partial class SessionTabViewModel : ObservableObject, IAsyncDisposable
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsConnected), nameof(CanReconnect), nameof(IsOverlayVisible))]
    private SessionState _state = SessionState.Connecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _error;

    [ObservableProperty]
    private string _title;

    /// <summary>The connection's icon, kept in step when the user changes it (tree or tab menu).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKey), nameof(IconBrushKey))]
    private NodeIcon? _icon;

    /// <summary>"#RRGGBB" strip under this tab's header; session-only, null = none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTabColor))]
    private string? _tabColor;

    protected SessionTabViewModel(ConnectionEntry connection)
    {
        Connection = connection;
        _title = connection.Name;
        _icon = connection.Icon;
        TabColorOptions =
        [
            new TabColorOption("None", null, new RelayCommand(() => TabColor = null)),
            .. Connections.IconCatalog.Colors.Select(c => new TabColorOption(c.Label, c.Hex, new RelayCommand(() => TabColor = c.Hex))),
        ];
    }

    public ConnectionEntry Connection { get; }

    public bool IsRdp => Connection.Kind == ConnectionKind.Rdp;

    public bool IsSsh => Connection.Kind == ConnectionKind.Ssh;

    public virtual string IconKey => Connections.IconCatalog.GlyphKey(Icon, ConnectionGlyph.IconKey(Connection.Kind));

    public string IconBrushKey => Connections.IconCatalog.BrushKey(Icon, ConnectionGlyph.BrushKey(Connection.Kind));

    public bool HasTabColor => TabColor is not null;

    /// <summary>The "Tab color" submenu.</summary>
    public IReadOnlyList<TabColorOption> TabColorOptions { get; }

    public bool IsConnected => State == SessionState.Connected;

    public bool CanReconnect => State is SessionState.Disconnected or SessionState.Failed;

    /// <summary>The "connecting… / failed — reconnect" card over the session surface.</summary>
    public virtual bool IsOverlayVisible => State != SessionState.Connected;

    public string StatusText => State switch
    {
        SessionState.Connecting => $"Connecting to {Connection.Host}…",
        SessionState.Connected => "Connected",
        SessionState.Disconnected => Error ?? "Disconnected",
        SessionState.Failed => Error ?? "Connection failed",
        _ => "",
    };

    /// <summary>Raised when the tab asks to be closed (e.g. the user typed `exit`).</summary>
    public event EventHandler? CloseRequested;

    [RelayCommand]
    public abstract Task ConnectAsync();

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    public abstract ValueTask DisposeAsync();
}
