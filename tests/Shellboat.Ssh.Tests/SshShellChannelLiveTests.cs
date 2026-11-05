using System.Collections.Concurrent;
using System.Text;
using Shellboat.Core.Connections;
using Shellboat.Core.Sessions;
using Shellboat.Core.Trust;

namespace Shellboat.Ssh.Tests;

/// <summary>Against the real sshd container (build/test-sshd.sh). Skip without it — a skip is not a pass.</summary>
public class SshShellChannelLiveTests
{
    private static ConnectionEntry Entry(SshSettings? ssh = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "test",
        Kind = ConnectionKind.Ssh,
        Host = SshTestServer.Host,
        Port = SshTestServer.Port,
        UserName = SshTestServer.User,
        Ssh = ssh ?? new SshSettings(SshAuthMethod.Password),
    };

    private static async Task<string> RunAsync(IShellChannel channel, string command)
    {
        var output = new ConcurrentQueue<byte[]>();
        channel.DataReceived += output.Enqueue;
        channel.Write(Encoding.UTF8.GetBytes(command + "\n"));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var text = "";
        while (DateTime.UtcNow < deadline)
        {
            text = Encoding.UTF8.GetString(output.SelectMany(b => b).ToArray());
            if (text.Contains("MARK-END"))
            {
                break;
            }

            await Task.Delay(50);
        }

        return text;
    }

    [SkippableFact]
    public async Task Password_login_opens_a_pty_shell_and_trusts_the_host_once()
    {
        SshTestServer.Require();
        var trust = new MemoryTrustStore();
        var prompter = new ScriptedPrompter(true);

        await using (var channel = new SshShellChannel(Entry(), new SshCredentials(SshTestServer.Password, null), trust, prompter))
        {
            await channel.ConnectAsync(100, 30, default);
            var text = await RunAsync(channel, "echo $TERM; stty size; echo MARK-END");
            Assert.Contains("xterm-256color", text);
            Assert.Contains("30 100", text);
        }

        Assert.Single(prompter.Prompts);
        Assert.Equal(TrustVerdict.Unknown, prompter.Prompts[0].Verdict);
        Assert.StartsWith("ssh-", Assert.Single(trust.Known.Values));

        // Second connect: key is known, no prompt.
        await using (var again = new SshShellChannel(Entry(), new SshCredentials(SshTestServer.Password, null), trust, prompter))
        {
            await again.ConnectAsync(80, 24, default);
        }

        Assert.Single(prompter.Prompts);
    }

    [SkippableFact]
    public async Task Resize_reaches_the_remote_pty()
    {
        SshTestServer.Require();
        await using var channel = new SshShellChannel(Entry(), new SshCredentials(SshTestServer.Password, null), new MemoryTrustStore(), new ScriptedPrompter(true));
        await channel.ConnectAsync(80, 24, default);
        channel.Resize(132, 43);
        await Task.Delay(300);
        Assert.Contains("43 132", await RunAsync(channel, "stty size; echo MARK-END"));
    }

    [SkippableFact]
    public async Task Wrong_password_is_an_authentication_failure()
    {
        SshTestServer.Require();
        await using var channel = new SshShellChannel(Entry(), new SshCredentials("nope", null), new MemoryTrustStore(), new ScriptedPrompter(true));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => channel.ConnectAsync(80, 24, default));
    }

    [SkippableFact]
    public async Task Declined_host_key_aborts()
    {
        SshTestServer.Require();
        await using var channel = new SshShellChannel(Entry(), new SshCredentials(SshTestServer.Password, null), new MemoryTrustStore(), new ScriptedPrompter(false));
        await Assert.ThrowsAsync<HostKeyRejectedException>(() => channel.ConnectAsync(80, 24, default));
    }

    [SkippableFact]
    public async Task Changed_host_key_is_a_hard_block_without_prompting()
    {
        SshTestServer.Require();
        var trust = new MemoryTrustStore();
        trust.Known[(SshTestServer.Host, SshTestServer.Port)] = "ssh-ed25519 SHA256:not-the-real-one";
        var prompter = new ScriptedPrompter(true);
        await using var channel = new SshShellChannel(Entry(), new SshCredentials(SshTestServer.Password, null), trust, prompter);

        var ex = await Assert.ThrowsAsync<HostKeyRejectedException>(() => channel.ConnectAsync(80, 24, default));
        Assert.Contains("CHANGED", ex.Message);
        Assert.Empty(prompter.Prompts);
    }

    [SkippableFact]
    public async Task Private_key_login_without_passphrase()
    {
        SshTestServer.Require();
        var key = Path.Combine(SshTestServer.KeyDir, "id_ed25519");
        Skip.IfNot(File.Exists(key), "test key missing — run ./build/test-sshd.sh");
        await using var channel = new SshShellChannel(Entry(new SshSettings(SshAuthMethod.PrivateKey, key)), new SshCredentials(null, null), new MemoryTrustStore(), new ScriptedPrompter(true));
        await channel.ConnectAsync(80, 24, default);
        Assert.Contains("shellboat", await RunAsync(channel, "whoami; echo MARK-END"));
    }

    [SkippableFact]
    public async Task Encrypted_key_needs_its_passphrase()
    {
        SshTestServer.Require();
        var key = Path.Combine(SshTestServer.KeyDir, "id_ed25519_pass");
        Skip.IfNot(File.Exists(key), "test key missing — run ./build/test-sshd.sh");
        var entry = Entry(new SshSettings(SshAuthMethod.PrivateKey, key));

        await using (var without = new SshShellChannel(entry, new SshCredentials(null, null), new MemoryTrustStore(), new ScriptedPrompter(true)))
        {
            await Assert.ThrowsAsync<AuthenticationFailedException>(() => without.ConnectAsync(80, 24, default));
        }

        await using var with = new SshShellChannel(entry, new SshCredentials(null, "testpass"), new MemoryTrustStore(), new ScriptedPrompter(true));
        await with.ConnectAsync(80, 24, default);
    }

    [SkippableFact]
    public async Task Unreachable_host_is_an_io_error_not_an_auth_failure()
    {
        var entry = Entry() with { Host = "127.0.0.1", Port = 1 };
        await using var channel = new SshShellChannel(entry, new SshCredentials("x", null), new MemoryTrustStore(), new ScriptedPrompter(true));
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => channel.ConnectAsync(80, 24, default));
        Assert.IsNotType<AuthenticationFailedException>(ex);
    }
}
