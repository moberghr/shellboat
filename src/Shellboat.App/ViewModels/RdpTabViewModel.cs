using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Shellboat.App.Rdp;
using Shellboat.App.Services;
using Shellboat.Core.Connections;
using Shellboat.Core.Secrets;
using Shellboat.Rdp;

namespace Shellboat.App.ViewModels;

/// <summary>
/// An RDP desktop tab. Adapted from RDPilot's RdpSessionViewModel: the native runtime decodes on its own
/// thread and signals frames; <see cref="ManagedFramePresenter"/> copies dirty regions into a
/// <see cref="WriteableBitmap"/> on the UI thread; the remote cursor arrives out of band.
/// </summary>
public sealed partial class RdpTabViewModel : SessionTabViewModel, IRdpInputTarget
{
    private readonly CredentialResolver _credentials;
    private readonly IDialogService _dialogs;
    private readonly object _cursorLock = new();
    private RdpSessionRuntime? _runtime;
    private ManagedFramePresenter? _presenter;
    private RemoteCursorCache? _cursorCache;
    private RemoteCursorDescriptor? _pendingCursor;
    private int _cursorApplyQueued;
    private double _renderScaling = 1.0;
    private int _viewportWidth = 1280;
    private int _viewportHeight = 800;
    private bool _authRetried;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayWidth), nameof(DisplayHeight))]
    private WriteableBitmap? _screen;

    [ObservableProperty]
    private Cursor? _remoteCursor;

    public RdpTabViewModel(ConnectionEntry connection, CredentialResolver credentials, IDialogService dialogs)
        : base(connection)
    {
        _credentials = credentials;
        _dialogs = dialogs;
    }

    /// <summary>Raised when a new frame was copied into <see cref="Screen"/>; the view invalidates the image.</summary>
    public event EventHandler? RedrawRequested;

    /// <summary>Remote clipboard text arrived (RDP thread); the view writes it to the local clipboard.</summary>
    public event EventHandler<string>? RemoteClipboardText;

    public double DisplayWidth => Screen is null ? 0 : Screen.PixelSize.Width / _renderScaling;

    public double DisplayHeight => Screen is null ? 0 : Screen.PixelSize.Height / _renderScaling;

    public double RenderScaling => _renderScaling;

    /// <summary>The viewport size in physical pixels, reported by the view before and during the session.</summary>
    public void UpdateViewport(int width, int height, double renderScaling)
    {
        if (renderScaling > 0 && Math.Abs(renderScaling - _renderScaling) > 0.001)
        {
            _renderScaling = renderScaling;
            _presenter?.UpdateRenderScaling(renderScaling);
            OnPropertyChanged(nameof(DisplayWidth));
            OnPropertyChanged(nameof(DisplayHeight));
        }

        if (width <= 0 || height <= 0)
        {
            return;
        }

        _viewportWidth = width;
        _viewportHeight = height;
        if (Connection.Rdp?.Resolution != RdpResolutionMode.Fixed)
        {
            _runtime?.Resize(width, height);
        }
    }

    public override async Task ConnectAsync()
    {
        if (State == SessionState.Connected)
        {
            return;
        }

        TearDown();
        State = SessionState.Connecting;
        Error = null;
        _authRetried = false;

        var password = await _credentials.GetSavedAsync(Connection, SecretPurpose.Password, CancellationToken.None)
            ?? await _credentials.PromptAsync(Connection, SecretPurpose.Password, null, CancellationToken.None);
        if (password is null)
        {
            Error = "Cancelled.";
            State = SessionState.Failed;
            return;
        }

        Start(password);
    }

    private void Start(string password)
    {
        var request = new RdpConnectRequest(Connection, password, _viewportWidth, _viewportHeight, _renderScaling);
        var runtime = new RdpSessionRuntime(Connection.Host, DecideCertificate);
        _presenter = new ManagedFramePresenter(Connection.Name, request.Width, request.Height,
            screen => Screen = screen,
            () => RedrawRequested?.Invoke(this, EventArgs.Empty),
            runtime.PresentInto,
            _renderScaling);
        _cursorCache = new RemoteCursorCache(runtime.CopyCursor);
        runtime.FrameReceived += (w, h) => _presenter?.EnqueueFrame(w, h);
        runtime.StatusReceived += (status, error) => OnStatus(runtime, status, error);
        runtime.CursorReceived += OnCursor;
        runtime.ClipboardTextReceived += text => RemoteClipboardText?.Invoke(this, text);
        _runtime = runtime;

        var error = runtime.Start(request);
        if (error is not null)
        {
            Error = error.Message;
            State = SessionState.Failed;
        }
    }

    public void Send(PointerInput input)
    {
        if (_runtime?.Send(input) == true)
        {
            _presenter?.MarkInputSent();
        }
    }

    public void Send(KeyInput input)
    {
        if (_runtime?.Send(input) == true)
        {
            _presenter?.MarkInputSent();
        }
    }

    public void OfferLocalClipboardText(string text) => _runtime?.OfferText(text);

    /// <summary>Ctrl+Alt+Del can never be intercepted locally, so it is an explicit action.</summary>
    public void SendCtrlAltDel()
    {
        const ushort leftCtrl = 0x1D;
        const ushort leftAlt = 0x38;
        const ushort delete = 0x53;
        const ushort release = 0x8000;
        const ushort extended = 0x0100;
        Send(new KeyInput(0, leftCtrl));
        Send(new KeyInput(0, leftAlt));
        Send(new KeyInput(extended, delete));
        Send(new KeyInput(release | extended, delete));
        Send(new KeyInput(release, leftAlt));
        Send(new KeyInput(release, leftCtrl));
    }

    public void SuspendPresentation() => _presenter?.Suspend();

    public void ResumePresentation()
    {
        _runtime?.RequestFullFrame();
        _presenter?.Resume();
    }

    /// <summary>RDP thread; blocks it while the user decides, which FreeRDP expects.</summary>
    private CertificateTrustDecision DecideCertificate(RdpCertificatePrompt prompt)
        => _dialogs.ConfirmRdpCertificateAsync(prompt).GetAwaiter().GetResult();

    private void OnStatus(RdpSessionRuntime source, RdpSessionStatus status, RdpSessionError? error)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            // A torn-down runtime can still have a status in flight; only the current one speaks.
            if (!ReferenceEquals(_runtime, source))
            {
                return;
            }

            switch (status)
            {
                case RdpSessionStatus.Connected:
                    Error = null;
                    State = SessionState.Connected;
                    break;
                case RdpSessionStatus.Failed when error?.Kind == RdpSessionErrorKind.Authentication && !_authRetried:
                    // Wrong saved password: ask once more, saving the new one if the user wants.
                    _authRetried = true;
                    TearDown();
                    var password = await _credentials.PromptAsync(Connection, SecretPurpose.Password, error.Message, CancellationToken.None);
                    if (password is null)
                    {
                        Error = error.Message;
                        State = SessionState.Failed;
                        return;
                    }

                    Start(password);
                    break;
                case RdpSessionStatus.Failed:
                    Error = error?.Message ?? "Connection failed.";
                    State = SessionState.Failed;
                    break;
                case RdpSessionStatus.Disconnected:
                    Error = "Disconnected.";
                    State = SessionState.Disconnected;
                    break;
            }
        });
    }

    /// <summary>RDP thread: coalesce to at most one UI-thread apply (RDPilot's pattern).</summary>
    private void OnCursor(RemoteCursorDescriptor cursor)
    {
        lock (_cursorLock)
        {
            _pendingCursor = cursor;
        }

        if (Interlocked.Exchange(ref _cursorApplyQueued, 1) == 0)
        {
            Dispatcher.UIThread.Post(ApplyPendingCursor);
        }
    }

    private void ApplyPendingCursor()
    {
        Interlocked.Exchange(ref _cursorApplyQueued, 0);
        RemoteCursorDescriptor? descriptor;
        lock (_cursorLock)
        {
            descriptor = _pendingCursor;
            _pendingCursor = null;
        }

        if (descriptor is not { } c || _cursorCache is null)
        {
            return;
        }

        if (_cursorCache.Resolve(c.Kind, c.CursorId, c.Width, c.Height, c.HotX, c.HotY) is { } resolved)
        {
            RemoteCursor = resolved;
        }
    }

    private void TearDown()
    {
        var runtime = _runtime;
        _runtime = null;
        _presenter?.Dispose();
        _presenter = null;
        RemoteCursor = null;
        _cursorCache?.Dispose();
        _cursorCache = null;
        runtime?.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        var runtime = _runtime;
        if (runtime is not null && runtime.HasHandle)
        {
            await runtime.DisconnectAsync();
        }

        TearDown();
    }
}
