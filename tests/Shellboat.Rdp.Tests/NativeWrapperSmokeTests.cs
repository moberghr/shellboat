using Shellboat.Core.Connections;

namespace Shellboat.Rdp.Tests;

/// <summary>
/// Drives the real FreeRDP wrapper (built by build/native.sh) against a closed port: proves the library loads,
/// the struct/callback ABI lines up, the connection thread runs and reports a typed failure. Skips when the
/// native library has not been built.
/// </summary>
public class NativeWrapperSmokeTests
{
    [SkippableFact]
    public async Task Connecting_to_a_closed_port_reports_failed_status_through_the_native_callbacks()
    {
        var lib = OperatingSystem.IsWindows() ? "freerdp_wrapper.dll" : OperatingSystem.IsMacOS() ? "libfreerdp_wrapper.dylib" : "libfreerdp_wrapper.so";
        Skip.IfNot(File.Exists(Path.Combine(AppContext.BaseDirectory, lib)), "native wrapper not built — run ./build/native.sh");

        var entry = new ConnectionEntry
        {
            Id = Guid.NewGuid(), Name = "closed", Kind = ConnectionKind.Rdp, Host = "127.0.0.1", Port = 1,
            UserName = "nobody", Rdp = new RdpSettings(),
        };
        var done = new TaskCompletionSource<(RdpSessionStatus, RdpSessionError?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var runtime = new RdpSessionRuntime(entry.Host, _ => CertificateTrustDecision.Reject);
        runtime.StatusReceived += (s, e) =>
        {
            if (s is RdpSessionStatus.Failed or RdpSessionStatus.Disconnected)
            {
                done.TrySetResult((s, e));
            }
        };

        Assert.Null(runtime.Start(new RdpConnectRequest(entry, "pw", 1024, 768, 1.0)));
        var finished = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.Same(done.Task, finished);
        var (status, error) = await done.Task;
        Assert.Equal(RdpSessionStatus.Failed, status);
        Assert.NotNull(error);
    }
}
