using System.Runtime.InteropServices;
using Shellboat.Rdp.Native;

namespace Shellboat.Rdp;

/// <summary>
/// Owns one native RDP session and roots its callback delegates for the session's whole lifetime. Ported
/// from RDPilot's RdpSessionRuntime (file clipboard removed — out of v1 scope; the native callbacks for it
/// are still supplied as no-ops because the ABI requires them).
/// </summary>
public sealed class RdpSessionRuntime : IDisposable
{
    private readonly Func<RdpConnectRequest, NativeSessionCallbacks, INativeRdpSession> _connect;
    private readonly Func<RdpCertificatePrompt, CertificateTrustDecision> _decideCertificate;
    private readonly NativeSessionCallbacks _callbacks;
    private readonly object _lifetimeLock = new();
    private readonly string _host;
    private INativeRdpSession? _session;
    private IntPtr _handle;
    private int _initializing;
    private int _disposeStarted;
    private bool _started;

    /// <param name="decideCertificate">Called on the RDP thread; may block while the user decides.</param>
    public RdpSessionRuntime(string host, Func<RdpCertificatePrompt, CertificateTrustDecision> decideCertificate)
        : this(host, decideCertificate, null)
    {
    }

    internal RdpSessionRuntime(string host, Func<RdpCertificatePrompt, CertificateTrustDecision> decideCertificate,
        Func<RdpConnectRequest, NativeSessionCallbacks, INativeRdpSession>? connect)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _decideCertificate = decideCertificate ?? throw new ArgumentNullException(nameof(decideCertificate));
        _connect = connect ?? NativeRdpSession.Connect;
        _callbacks = new NativeSessionCallbacks(
            OnFrame, OnClipboardText, (_, _, _, _) => { }, OnStatus, OnCertificate, OnCursor, (_, _, _, _, _) => { });
    }

    /// <summary>A frame is ready to present (RDP thread). Args: framebuffer width, height.</summary>
    public event Action<int, int>? FrameReceived;

    public event Action<string>? ClipboardTextReceived;

    public event Action<RdpSessionStatus, RdpSessionError?>? StatusReceived;

    public event Action<RemoteCursorDescriptor>? CursorReceived;

    public bool HasHandle => !IsDisposeStarted && Volatile.Read(ref _handle) != IntPtr.Zero;

    private bool IsDisposeStarted => Volatile.Read(ref _disposeStarted) != 0;

    /// <returns>Null when the native connect started (status arrives via <see cref="StatusReceived"/>).</returns>
    public RdpSessionError? Start(RdpConnectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_lifetimeLock)
        {
            if (_started || IsDisposeStarted)
            {
                throw new InvalidOperationException("This runtime cannot be started again.");
            }

            _started = true;
            Volatile.Write(ref _initializing, 1);
        }

        try
        {
            INativeRdpSession session;
            try
            {
                session = _connect(request, _callbacks);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                return new RdpSessionError(0, "WRAPPER_NATIVE_LOAD_FAILED",
                    $"The RDP engine (FreeRDP wrapper) could not be loaded: {ex.Message}. Build it with build/native.sh.",
                    RdpSessionErrorKind.Unknown);
            }

            lock (_lifetimeLock)
            {
                if (!IsDisposeStarted)
                {
                    _session = session;
                    Volatile.Write(ref _handle, session.Handle);
                    if (session.Handle != IntPtr.Zero)
                    {
                        return null;
                    }
                }
            }

            // Dispose can win while the connector runs. Never publish a handle afterwards.
            if (IsDisposeStarted && session.Handle != IntPtr.Zero)
            {
                session.Free();
            }

            GC.KeepAlive(_callbacks);
            return new RdpSessionError(0, "WRAPPER_SESSION_CREATE_FAILED", "Failed to start the RDP session.", RdpSessionErrorKind.Unknown);
        }
        finally
        {
            Volatile.Write(ref _initializing, 0);
        }
    }

    public bool Send(KeyInput input)
    {
        var session = ActiveSession;
        session?.SendKeyboardEvent(input.Flags, input.ScanCode);
        return session != null;
    }

    public bool Send(PointerInput input)
    {
        var session = ActiveSession;
        session?.SendMouseEvent(input.Flags, input.X, input.Y);
        return session != null;
    }

    public void OfferText(string text) => ActiveSession?.SetLocalClipboardText(text);

    public void Resize(int width, int height)
    {
        var session = ActiveSession;
        if (session == null || width <= 0 || height <= 0)
        {
            return;
        }

        (width, height) = RdpSessionOptions.NormalizeResolution(width, height);
        session.UpdateResolution(width, height);
    }

    public void RequestFullFrame() => ActiveSession?.RequestFullFrame();

    public FramePresentResult PresentInto(IntPtr dest, int destStride, int destWidth, int destHeight)
        => ActiveSession?.Present(dest, destStride, destWidth, destHeight) ?? default;

    public bool CopyCursor(uint cursorId, IntPtr dest, int destStride, int destWidth, int destHeight)
        => ActiveSession?.CopyCursorImage(cursorId, dest, destStride, destWidth, destHeight) == true;

    public Task DisconnectAsync()
    {
        var session = ActiveSession;
        return session == null ? Task.CompletedTask : Task.Run(session.Disconnect);
    }

    private INativeRdpSession? ActiveSession
    {
        get
        {
            var session = _session;
            return HasHandle ? session : null;
        }
    }

    private bool IsActive(IntPtr handle) => HasHandle && handle == Volatile.Read(ref _handle);

    private bool IsActiveOrInitializing(IntPtr handle)
        => IsActive(handle) || (!IsDisposeStarted && handle != IntPtr.Zero && Volatile.Read(ref _initializing) != 0);

    private void OnFrame(IntPtr handle, IntPtr data, int width, int height, int dirtyX, int dirtyY, int dirtyWidth, int dirtyHeight, int sourceStride)
    {
        // `data` aliases the live framebuffer and must not be touched here; the UI pulls pixels via PresentInto.
        if (IsActive(handle))
        {
            FrameReceived?.Invoke(width, height);
        }
    }

    private void OnClipboardText(IntPtr handle, IntPtr text)
    {
        if (!IsActive(handle))
        {
            return;
        }

        var value = Marshal.PtrToStringUTF8(text) ?? "";
        if (!IsDisposeStarted)
        {
            ClipboardTextReceived?.Invoke(value);
        }
    }

    private void OnStatus(IntPtr handle, int status, uint errorCode, IntPtr errorName, IntPtr errorMessage)
    {
        if (!IsActiveOrInitializing(handle))
        {
            return;
        }

        RdpSessionStatus? value = status switch
        {
            1 => RdpSessionStatus.Connected,
            2 => RdpSessionStatus.Failed,
            3 => RdpSessionStatus.Disconnected,
            _ => null,
        };
        if (value is not { } typed)
        {
            return;
        }

        var error = typed == RdpSessionStatus.Failed
            ? RdpSessionError.Create(errorCode, Marshal.PtrToStringUTF8(errorName), Marshal.PtrToStringUTF8(errorMessage))
            : null;
        StatusReceived?.Invoke(typed, error);
    }

    private void OnCursor(IntPtr handle, int kind, uint cursorId, int width, int height, int hotX, int hotY)
    {
        if (IsActive(handle))
        {
            CursorReceived?.Invoke(new RemoteCursorDescriptor((RemoteCursorKind)kind, cursorId, width, height, hotX, hotY));
        }
    }

    private int OnCertificate(IntPtr handle, IntPtr host, ushort port, IntPtr commonName, IntPtr subject,
        IntPtr issuer, IntPtr fingerprint, int isChanged, IntPtr previousSubject, IntPtr previousIssuer, IntPtr previousFingerprint)
    {
        if (!IsActiveOrInitializing(handle))
        {
            return (int)CertificateTrustDecision.Reject;
        }

        var prompt = new RdpCertificatePrompt(
            Marshal.PtrToStringUTF8(host) ?? _host,
            port,
            Marshal.PtrToStringUTF8(commonName) ?? "",
            Marshal.PtrToStringUTF8(subject) ?? "",
            Marshal.PtrToStringUTF8(issuer) ?? "",
            Marshal.PtrToStringUTF8(fingerprint) ?? "",
            isChanged != 0,
            Marshal.PtrToStringUTF8(previousSubject),
            Marshal.PtrToStringUTF8(previousIssuer),
            Marshal.PtrToStringUTF8(previousFingerprint));
        return IsDisposeStarted ? (int)CertificateTrustDecision.Reject : (int)_decideCertificate(prompt);
    }

    public void Dispose()
    {
        IntPtr handle;
        INativeRdpSession? session;
        lock (_lifetimeLock)
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            {
                return;
            }

            handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            session = Interlocked.Exchange(ref _session, null);
        }

        if (handle != IntPtr.Zero)
        {
            session?.Free();
        }

        GC.KeepAlive(_callbacks);
    }
}
