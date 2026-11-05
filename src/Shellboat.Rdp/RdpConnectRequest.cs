using System.Net;
using System.Net.Sockets;
using Shellboat.Core.Connections;

namespace Shellboat.Rdp;

/// <summary>
/// Normalised inputs for one RDP connect. The password lives here only for the attempt. Adapted from
/// RDPilot's RdpSessionStartRequest + RdpConnectPlan, minus gateway (out of v1 scope).
/// </summary>
public sealed record RdpConnectRequest
{
    public RdpConnectRequest(ConnectionEntry connection, string password, int width, int height, double renderScaling)
    {
        if (connection.Kind != ConnectionKind.Rdp)
        {
            throw new ArgumentException("Not an RDP connection.", nameof(connection));
        }

        Connection = connection;
        Password = password;
        var rdp = connection.Rdp ?? new RdpSettings();
        if (rdp.Resolution == RdpResolutionMode.Fixed)
        {
            (width, height) = (rdp.Width, rdp.Height);
        }

        (Width, Height) = RdpSessionOptions.NormalizeResolution(width, height);
        RenderScaling = double.IsFinite(renderScaling) && renderScaling > 0 ? renderScaling : 1.0;
        ColorDepth = RdpSessionOptions.NormalizeColorDepth(rdp.ColorDepth);
    }

    public ConnectionEntry Connection { get; }
    public string Password { get; }
    public int Width { get; }
    public int Height { get; }
    public double RenderScaling { get; }
    public int ColorDepth { get; }

    public override string ToString() => $"RdpConnectRequest {{ Host = {Connection.Host}, {Width}x{Height} }}";

    internal Native.NativeWrapper.RdpSessionConfig ToNativeConfig(Func<string, IPAddress[]> resolve, uint keyboardLayout)
    {
        var network = RdpSessionOptions.NormalizeNetworkSettings(RdpConnectionType.Lan);
        var desktopScale = RdpSessionOptions.ClampDpiScalePercent(RenderScaling * 100);
        var host = Connection.Host;
        return new Native.NativeWrapper.RdpSessionConfig
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.NativeWrapper.RdpSessionConfig>(),
            Host = host,
            // RDPilot pre-resolves only on Windows (its WinPR resolver misbehaves there); elsewhere FreeRDP resolves.
            ConnectHost = OperatingSystem.IsWindows() ? ResolveDirect(host, resolve) : host,
            Domain = Connection.Rdp?.Domain ?? "",
            User = Connection.UserName,
            Password = Password,
            GatewayHost = "",
            GatewayDomain = "",
            GatewayUser = "",
            GatewayPassword = "",
            Port = RdpSessionOptions.NormalizePort((ushort)Connection.EffectivePort),
            Width = Width,
            Height = Height,
            ColorDepth = ColorDepth,
            ConnectionType = (uint)network.ConnectionType,
            NetworkAutoDetect = network.NetworkAutoDetect,
            UseNetworkLevelAuthentication = RdpSessionOptions.ShouldUseNetworkLevelAuthentication(Connection.UserName, Password),
            KeyboardLayout = keyboardLayout,
            DesktopScalePercent = desktopScale,
            DeviceScalePercent = RdpSessionOptions.ToDeviceScalePercent(desktopScale),
            Compression = true,
            BitmapCache = true,
            FontSmoothing = true,
            DesktopWallpaper = false,
            Themes = true,
            MenuAnimations = false,
            FullWindowDrag = false,
        };
    }

    private static string ResolveDirect(string host, Func<string, IPAddress[]> resolve)
    {
        if (IPAddress.TryParse(host, out _))
        {
            return host;
        }

        try
        {
            var addresses = resolve(host);
            return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString()
                ?? addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6)?.ToString()
                ?? host;
        }
        catch (SocketException)
        {
            return host;
        }
    }
}
