using System.Net.Sockets;

namespace Shellboat.Ssh.Tests;

/// <summary>The container from build/test-sshd.sh. Override with SHELLBOAT_TEST_SSH_HOST / _PORT.</summary>
internal static class SshTestServer
{
    public static string Host => Environment.GetEnvironmentVariable("SHELLBOAT_TEST_SSH_HOST") ?? "127.0.0.1";

    public static int Port => int.TryParse(Environment.GetEnvironmentVariable("SHELLBOAT_TEST_SSH_PORT"), out var p) ? p : 2222;

    public const string User = "shellboat";
    public const string Password = "shellboat-test";

    public static string KeyDir
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Combine(dir, "Shellboat.slnx")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            return Path.Combine(dir ?? ".", "build", ".test-sshd");
        }
    }

    public static void Require()
    {
        try
        {
            using var client = new TcpClient();
            if (!client.ConnectAsync(Host, Port).Wait(TimeSpan.FromSeconds(1)))
            {
                throw new SkipException($"No test sshd on {Host}:{Port} — run ./build/test-sshd.sh");
            }
        }
        catch (AggregateException)
        {
            throw new SkipException($"No test sshd on {Host}:{Port} — run ./build/test-sshd.sh");
        }
    }
}
