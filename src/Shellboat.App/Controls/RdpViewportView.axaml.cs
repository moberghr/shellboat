using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Shellboat.App.Rdp;
using Shellboat.App.ViewModels;
using Shellboat.Rdp;

namespace Shellboat.App.Controls;

/// <summary>
/// Shows one RDP tab's framebuffer and forwards keyboard/pointer input. Adapted from RDPilot's
/// RdpViewportView + RdpViewportPresenter (keyboard grab and file clipboard left out of v1). Code-behind
/// only wires events; protocol work is in <see cref="RdpTabViewModel"/>.
/// </summary>
public partial class RdpViewportView : UserControl
{
    private readonly ViewportResolutionService _resolution = new();
    private readonly ViewportResolutionUpdateScheduler _scheduler = new(action => Dispatcher.UIThread.Post(action));
    private readonly PointerInputSender _pointer;
    private readonly HashSet<Key> _pressed = [];
    private RdpTabViewModel? _vm;
    private string? _lastClipboard;
    private DispatcherTimer? _clipboardPoll;

    public RdpViewportView()
    {
        InitializeComponent();
        _pointer = new PointerInputSender(() => _vm, action => Dispatcher.UIThread.Post(action));
        ViewportHost.SizeChanged += (_, _) => QueueResolutionUpdate();
        RdpImage.PointerMoved += (_, e) => Pointer(e.GetPosition(RdpImage), p => _pointer.Move(p.X, p.Y));
        RdpImage.PointerPressed += (_, e) =>
        {
            RdpImage.Focus();
            var pos = e.GetPosition(RdpImage);
            _pointer.Press(pos.X, pos.Y, e.GetCurrentPoint(RdpImage).Properties);
        };
        RdpImage.PointerReleased += (_, e) =>
        {
            var pos = e.GetPosition(RdpImage);
            _pointer.Release(pos.X, pos.Y, e.GetCurrentPoint(RdpImage).Properties.PointerUpdateKind);
        };
        RdpImage.PointerWheelChanged += (_, e) =>
        {
            var pos = e.GetPosition(RdpImage);
            _pointer.Wheel(pos.X, pos.Y, e.Delta);
            e.Handled = true;
        };
        RdpImage.LostFocus += (_, _) => ReleasePressedKeys();
        RdpImage.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        RdpImage.AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.RedrawRequested -= OnRedraw;
            _vm.RemoteClipboardText -= OnRemoteClipboard;
            _vm.SuspendPresentation();
        }

        _vm = DataContext as RdpTabViewModel;
        if (_vm is not null)
        {
            _vm.RedrawRequested += OnRedraw;
            _vm.RemoteClipboardText += OnRemoteClipboard;
            _vm.ResumePresentation();
            QueueResolutionUpdate();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Text clipboard local → remote: poll while attached (Avalonia has no clipboard-changed event).
        _clipboardPoll = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, async (_, _) => await PollClipboardAsync());
        _clipboardPoll.Start();
        QueueResolutionUpdate();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _clipboardPoll?.Stop();
        _clipboardPoll = null;
        ReleasePressedKeys();
        _scheduler.Cancel();
        _pointer.Cancel();
    }

    private void OnRedraw(object? sender, EventArgs e) => RdpImage.InvalidateVisual();

    private void OnRemoteClipboard(object? sender, string text)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                _lastClipboard = text;
                await clipboard.SetTextAsync(text);
            }
        });
    }

    private async Task PollClipboardAsync()
    {
        if (_vm is not { IsConnected: true } || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        string? text;
        try
        {
            text = await clipboard.TryGetTextAsync();
        }
        catch (Exception)
        {
            return;
        }

        if (text is not null && text != _lastClipboard)
        {
            _lastClipboard = text;
            _vm.OfferLocalClipboardText(text);
        }
    }

    private void QueueResolutionUpdate()
    {
        if (_vm is null)
        {
            return;
        }

        var top = TopLevel.GetTopLevel(this);
        var scale = top?.RenderScaling ?? 1.0;
        var minimized = top is Window { WindowState: WindowState.Minimized };
        if (_resolution.TryCompute(ViewportHost.Bounds.Size, scale, minimized, out var w, out var h, out var normalized))
        {
            var vm = _vm;
            if (vm.State == SessionState.Connected)
            {
                _scheduler.Schedule(w, h, normalized, (width, height, s) => vm.UpdateViewport(width, height, s));
            }
            else
            {
                vm.UpdateViewport(w, h, normalized); // before connect: just record the size to ask for
            }
        }
    }

    private static void Pointer(Point p, Action<Point> send) => send(p);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null || !RdpKeyboardInputMapper.TryMapKey(e.Key, out var scancode))
        {
            return;
        }

        var flags = RdpKeyboardInputMapper.BuildKeyFlags(scancode, isRelease: false, out var code);
        _pressed.Add(e.Key);
        _vm.Send(new KeyInput(flags, code));
        e.Handled = true;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (_vm is null || !RdpKeyboardInputMapper.TryMapKey(e.Key, out var scancode))
        {
            return;
        }

        var flags = RdpKeyboardInputMapper.BuildKeyFlags(scancode, isRelease: true, out var code);
        _vm.Send(new KeyInput(flags, code));
        _pressed.Remove(e.Key);
        e.Handled = true;
    }

    /// <summary>Losing focus mid-chord would leave a modifier stuck down on the remote host.</summary>
    private void ReleasePressedKeys()
    {
        _pointer.Cancel();
        if (_vm is not null)
        {
            foreach (var key in _pressed)
            {
                if (RdpKeyboardInputMapper.TryMapKey(key, out var scancode))
                {
                    var flags = RdpKeyboardInputMapper.BuildKeyFlags(scancode, isRelease: true, out var code);
                    _vm.Send(new KeyInput(flags, code));
                }
            }
        }

        _pressed.Clear();
    }
}
