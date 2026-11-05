using System.Net;
using System.Runtime.InteropServices;

namespace Shellboat.Rdp.Native;

/// <summary>Seam over one native session handle so the runtime is testable without FreeRDP. From RDPilot.</summary>
internal interface INativeRdpSession
{
    IntPtr Handle { get; }

    void Disconnect();
    void Free();
    void UpdateResolution(int width, int height);
    void SendMouseEvent(ushort flags, ushort x, ushort y);
    void SendKeyboardEvent(ushort flags, ushort code);
    void SetLocalClipboardText(string? text);
    void RequestFullFrame();
    FramePresentResult Present(IntPtr dest, int destStride, int destWidth, int destHeight);
    bool CopyCursorImage(uint cursorId, IntPtr dest, int destStride, int destWidth, int destHeight);
}

internal sealed class NativeRdpSession : INativeRdpSession
{
    private NativeRdpSession(IntPtr handle) => Handle = handle;

    public IntPtr Handle { get; }

    public static INativeRdpSession Connect(RdpConnectRequest request, NativeSessionCallbacks callbacks)
    {
        var config = request.ToNativeConfig(Dns.GetHostAddresses, NativeWrapper.GetCurrentKeyboardLayout());
        var handle = NativeWrapper.rdp_session_connect(
            in config,
            callbacks.Frame,
            callbacks.ClipboardText,
            callbacks.ClipboardFiles,
            callbacks.Status,
            callbacks.Certificate,
            callbacks.Cursor,
            callbacks.ClipboardFileContents);
        return new NativeRdpSession(handle);
    }

    public void Disconnect() => NativeWrapper.rdp_session_disconnect(Handle);

    public void Free() => NativeWrapper.rdp_session_free(Handle);

    public void UpdateResolution(int width, int height) => NativeWrapper.rdp_session_update_resolution(Handle, width, height);

    public void SendMouseEvent(ushort flags, ushort x, ushort y) => NativeWrapper.rdp_session_send_mouse_event(Handle, flags, x, y);

    public void SendKeyboardEvent(ushort flags, ushort code) => NativeWrapper.rdp_session_send_keyboard_event(Handle, flags, code);

    public void SetLocalClipboardText(string? text) => NativeWrapper.rdp_session_clipboard_set_local_text(Handle, text);

    public void RequestFullFrame() => NativeWrapper.rdp_session_request_full_frame(Handle);

    public FramePresentResult Present(IntPtr dest, int destStride, int destWidth, int destHeight)
    {
        var presented = NativeWrapper.rdp_session_present(Handle, dest, destStride, destWidth, destHeight,
            out var dx, out var dy, out var dw, out var dh, out var fbW, out var fbH);
        return new FramePresentResult(presented, dx, dy, dw, dh, fbW, fbH);
    }

    public bool CopyCursorImage(uint cursorId, IntPtr dest, int destStride, int destWidth, int destHeight)
        => NativeWrapper.rdp_session_copy_cursor_image(Handle, cursorId, dest, destStride, destWidth, destHeight);
}

/// <summary>The native ABI's callback set, kept strongly reachable by the owning runtime for its lifetime.</summary>
internal sealed record NativeSessionCallbacks(
    NativeWrapper.FrameCallback Frame,
    NativeWrapper.ClipboardTextCallback ClipboardText,
    NativeWrapper.ClipboardFilesCallback ClipboardFiles,
    NativeWrapper.StatusCallback Status,
    NativeWrapper.CertificateDecisionCallback Certificate,
    NativeWrapper.CursorCallback Cursor,
    NativeWrapper.ClipboardFileContentsCallback ClipboardFileContents);
