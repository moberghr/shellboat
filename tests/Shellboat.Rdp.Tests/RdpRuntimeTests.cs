using Shellboat.Core.Connections;
using Shellboat.Rdp.Native;

namespace Shellboat.Rdp.Tests;

public class RdpRuntimeTests
{
    private static ConnectionEntry Entry(RdpSettings? rdp = null) => new()
    {
        Id = Guid.NewGuid(), Name = "dc", Kind = ConnectionKind.Rdp, Host = "dc.example", UserName = "admin",
        Rdp = rdp ?? new RdpSettings(),
    };

    [Fact]
    public void Start_publishes_the_handle_and_dispose_frees_it_once()
    {
        var native = new FakeNativeSession(new IntPtr(42));
        NativeSessionCallbacks? callbacks = null;
        var runtime = new RdpSessionRuntime("dc.example", _ => CertificateTrustDecision.Reject, (_, cb) =>
        {
            callbacks = cb;
            return native;
        });

        Assert.Null(runtime.Start(new RdpConnectRequest(Entry(), "pw", 1280, 800, 1.0)));
        Assert.True(runtime.HasHandle);
        Assert.NotNull(callbacks);

        runtime.Send(new KeyInput(0, 0x1E));
        Assert.Single(native.Keys);

        runtime.Dispose();
        runtime.Dispose();
        Assert.Equal(1, native.Frees);
        Assert.False(runtime.HasHandle);
        Assert.False(runtime.Send(new KeyInput(0, 0x1E)));
    }

    [Fact]
    public void Missing_native_library_is_reported_not_thrown()
    {
        var runtime = new RdpSessionRuntime("h", _ => CertificateTrustDecision.Reject, (_, _) => throw new DllNotFoundException("libfreerdp_wrapper"));
        var error = runtime.Start(new RdpConnectRequest(Entry(), "pw", 800, 600, 1.0));
        Assert.Equal("WRAPPER_NATIVE_LOAD_FAILED", error?.NativeName);
    }

    [Fact]
    public void Status_callbacks_map_to_typed_errors()
    {
        NativeSessionCallbacks? cb = null;
        var runtime = new RdpSessionRuntime("h", _ => CertificateTrustDecision.Reject, (_, c) =>
        {
            cb = c;
            return new FakeNativeSession(new IntPtr(7));
        });
        runtime.Start(new RdpConnectRequest(Entry(), "pw", 800, 600, 1.0));
        (RdpSessionStatus Status, RdpSessionError? Error)? seen = null;
        runtime.StatusReceived += (s, e) => seen = (s, e);

        var name = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8("FREERDP_ERROR_CONNECT_LOGON_FAILURE");
        try
        {
            cb!.Status(new IntPtr(7), 2, 0x2000C, name, IntPtr.Zero);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeCoTaskMem(name);
        }

        Assert.Equal(RdpSessionStatus.Failed, seen?.Status);
        Assert.Equal(RdpSessionErrorKind.Authentication, seen?.Error?.Kind);
    }

    [Fact]
    public void Resize_is_normalised_to_even_widths_within_limits()
    {
        var native = new FakeNativeSession(new IntPtr(1));
        var runtime = new RdpSessionRuntime("h", _ => CertificateTrustDecision.Reject, (_, _) => native);
        runtime.Start(new RdpConnectRequest(Entry(), "pw", 800, 600, 1.0));
        runtime.Resize(1281, 50);
        Assert.Equal((1280, 200), Assert.Single(native.Resizes));
    }

    [Fact]
    public void Fixed_resolution_ignores_the_viewport_and_nla_follows_credentials()
    {
        var fixedEntry = Entry(new RdpSettings(Resolution: RdpResolutionMode.Fixed, Width: 1600, Height: 900, ColorDepth: 15));
        var request = new RdpConnectRequest(fixedEntry, "pw", 3000, 2000, 2.0);
        Assert.Equal((1600, 900), (request.Width, request.Height));
        Assert.Equal(16, request.ColorDepth); // 15 is not a supported depth

        var config = request.ToNativeConfig(_ => [], 0);
        Assert.True(config.UseNetworkLevelAuthentication);
        Assert.Equal(200u, config.DesktopScalePercent);
        Assert.Equal(180u, config.DeviceScalePercent);
        Assert.Equal((ushort)3389, config.Port);
        Assert.DoesNotContain("pw", config.ToString());
        Assert.DoesNotContain("pw", request.ToString());

        var noPassword = new RdpConnectRequest(Entry(), "", 800, 600, 1.0).ToNativeConfig(_ => [], 0);
        Assert.False(noPassword.UseNetworkLevelAuthentication);
    }

    [Theory]
    [InlineData(1.0, 100u, 100u)]
    [InlineData(1.25, 125u, 140u)]
    [InlineData(1.5, 150u, 140u)]
    [InlineData(1.75, 175u, 180u)]
    public void Dpi_scale_factors_follow_the_protocol_ranges(double scale, uint desktop, uint device)
    {
        var config = new RdpConnectRequest(Entry(), "pw", 800, 600, scale).ToNativeConfig(_ => [], 0);
        Assert.Equal((desktop, device), (config.DesktopScalePercent, config.DeviceScalePercent));
    }
}
