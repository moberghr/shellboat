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

    protected SessionTabViewModel(ConnectionEntry connection)
    {
        Connection = connection;
        _title = connection.Name;
    }

    public ConnectionEntry Connection { get; }

    public bool IsRdp => Connection.Kind == ConnectionKind.Rdp;

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
