using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Shellboat.Rdp.Native;

/// <summary>
/// P/Invoke surface of <c>native/freerdp-wrapper</c> (vendored from RDPilot; see its NOTICE.md). Struct and
/// callback layouts must match <c>freerdp_wrapper.h</c> exactly. Ported from RDPilot's NativeWrapper.cs.
/// </summary>
[SuppressMessage("Interoperability", "SYSLIB1054", Justification = "Matches the upstream port; delegates are marshalled callbacks.")]
internal static class NativeWrapper
{
    private const string LibName = "freerdp_wrapper";

    /// <summary>Overrides where the wrapper library is loaded from (a directory).</summary>
    public const string NativeDirVariable = "SHELLBOAT_NATIVE_DIR";

    static NativeWrapper()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeWrapper).Assembly, Resolve);
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != LibName)
        {
            return IntPtr.Zero;
        }

        var file = OperatingSystem.IsWindows() ? "freerdp_wrapper.dll"
            : OperatingSystem.IsMacOS() ? "libfreerdp_wrapper.dylib"
            : "libfreerdp_wrapper.so";

        foreach (var dir in CandidateDirectories())
        {
            var path = Path.Combine(dir, file);
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero; // fall back to the default probing, which reports DllNotFoundException
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        if (Environment.GetEnvironmentVariable(NativeDirVariable) is { Length: > 0 } configured)
        {
            yield return configured;
        }

        yield return AppContext.BaseDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
    }

    public static uint GetCurrentKeyboardLayout()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0; // FreeRDP's default (US); per-OS layout detection is a later item.
        }

        var layoutName = new StringBuilder(9);
        if (GetKeyboardLayoutName(layoutName)
            && uint.TryParse(layoutName.ToString(), System.Globalization.NumberStyles.HexNumber, null, out var parsed))
        {
            return parsed;
        }

        return (uint)((ulong)GetKeyboardLayout(0).ToInt64() & 0xFFFF);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FrameCallback(IntPtr session, IntPtr data, int width, int height, int dirtyX, int dirtyY, int dirtyWidth, int dirtyHeight, int sourceStride);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ClipboardTextCallback(IntPtr session, IntPtr text);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ClipboardFilesCallback(IntPtr session, uint generation, IntPtr descriptor, nint descriptorSize);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ClipboardFileContentsCallback(IntPtr session, uint streamId, [MarshalAs(UnmanagedType.I1)] bool ok, IntPtr data, uint size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void StatusCallback(IntPtr session, int status, uint errorCode, IntPtr errorName, IntPtr errorMessage);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void CursorCallback(IntPtr session, int kind, uint cursorId, int width, int height, int hotX, int hotY);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int CertificateDecisionCallback(
        IntPtr session, IntPtr host, ushort port, IntPtr commonName, IntPtr subject, IntPtr issuer, IntPtr fingerprint,
        int isChanged, IntPtr previousSubject, IntPtr previousIssuer, IntPtr previousFingerprint);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RdpSessionConfig
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string? Host;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string? ConnectHost;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string? Domain;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string? User;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string? Password;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string? GatewayHost;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string? GatewayDomain;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string? GatewayUser;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string? GatewayPassword;
        public ushort Port;
        public int Width;
        public int Height;
        public int ColorDepth;
        public uint ConnectionType;
        public uint KeyboardLayout;
        public uint DesktopScalePercent;
        public uint DeviceScalePercent;
        [MarshalAs(UnmanagedType.U1)] public bool NetworkAutoDetect;
        [MarshalAs(UnmanagedType.U1)] public bool UseNetworkLevelAuthentication;
        [MarshalAs(UnmanagedType.U1)] public bool Compression;
        [MarshalAs(UnmanagedType.U1)] public bool BitmapCache;
        [MarshalAs(UnmanagedType.U1)] public bool FontSmoothing;
        [MarshalAs(UnmanagedType.U1)] public bool DesktopWallpaper;
        [MarshalAs(UnmanagedType.U1)] public bool Themes;
        [MarshalAs(UnmanagedType.U1)] public bool MenuAnimations;
        [MarshalAs(UnmanagedType.U1)] public bool FullWindowDrag;

        /// <summary>Never print the password.</summary>
        public override readonly string ToString() => $"RdpSessionConfig {{ Host = {Host}, Port = {Port}, User = {User} }}";
    }

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr rdp_session_connect(
        in RdpSessionConfig config,
        FrameCallback frameCallback,
        ClipboardTextCallback clipboardCallback,
        ClipboardFilesCallback clipboardFilesCallback,
        StatusCallback statusCallback,
        CertificateDecisionCallback certificateDecisionCallback,
        CursorCallback cursorCallback,
        ClipboardFileContentsCallback clipboardFileContentsCallback);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rdp_session_disconnect(IntPtr session);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rdp_session_free(IntPtr session);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rdp_session_update_resolution(IntPtr session, int width, int height);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rdp_session_send_mouse_event(IntPtr session, ushort flags, ushort x, ushort y);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rdp_session_send_keyboard_event(IntPtr session, ushort flags, ushort code);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rdp_session_clipboard_set_local_text(IntPtr session, [MarshalAs(UnmanagedType.LPUTF8Str)] string? text);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rdp_session_request_full_frame(IntPtr session);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool rdp_session_present(
        IntPtr session, IntPtr dest, int destStride, int destWidth, int destHeight,
        out int dirtyX, out int dirtyY, out int dirtyWidth, out int dirtyHeight, out int fbWidth, out int fbHeight);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool rdp_session_copy_cursor_image(IntPtr session, uint cursorId, IntPtr dest, int destStride, int destWidth, int destHeight);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetKeyboardLayoutName(StringBuilder pwszKLID);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);
}
