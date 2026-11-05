using System.Collections.Concurrent;
using System.Text;
using Shellboat.Core.Connections;
using Shellboat.Core.Sessions;

namespace Shellboat.Ssh.Tests;

/// <summary>SFTP against the real sshd container (build/test-sshd.sh). Skip without it — a skip is not a pass.</summary>
public class SshFileChannelLiveTests
{
    private static ConnectionEntry Entry() => new()
    {
        Id = Guid.NewGuid(),
        Name = "test",
        Kind = ConnectionKind.Ssh,
        Host = SshTestServer.Host,
        Port = SshTestServer.Port,
        UserName = SshTestServer.User,
        Ssh = new SshSettings(SshAuthMethod.Password),
    };

    private static SshCredentials Password => new(SshTestServer.Password, null);

    /// <summary>Creates a file on the server through a shell, so the SFTP side only reads.</summary>
    private static async Task WriteRemoteFileAsync(string name, string content, MemoryTrustStore trust)
    {
        await using var shell = new SshShellChannel(Entry(), Password, trust, new ScriptedPrompter(true));
        var output = new ConcurrentQueue<byte[]>();
        shell.DataReceived += output.Enqueue;
        await shell.ConnectAsync(80, 24, default);
        shell.Write(Encoding.UTF8.GetBytes($"mkdir -p sftp-live && printf '{content}' > sftp-live/{name} && echo MARK-\"DONE\"\n")); // quoted: the PTY echo of this line must not match the wait below
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !Encoding.UTF8.GetString(output.SelectMany(b => b).ToArray()).Contains("MARK-DONE\r\n"))
        {
            await Task.Delay(50);
        }
    }

    [SkippableFact]
    public async Task Lists_home_and_downloads_a_file_with_progress()
    {
        SshTestServer.Require();
        var trust = new MemoryTrustStore();
        var content = "hello from sftp " + Guid.NewGuid().ToString("N");
        await WriteRemoteFileAsync("hello.txt", content, trust);

        var prompter = new ScriptedPrompter(true);
        await using var sftp = new SshFileChannel(Entry(), Password, trust, prompter);
        await sftp.ConnectAsync(default);

        Assert.True(sftp.IsConnected);
        Assert.StartsWith("/", sftp.HomeDirectory);
        Assert.Empty(prompter.Prompts); // the shell already trusted the host key; same store, no second prompt

        var home = await sftp.ListAsync(sftp.HomeDirectory, default);
        var dir = Assert.Single(home, e => e.Name == "sftp-live");
        Assert.True(dir.IsDirectory);
        Assert.DoesNotContain(home, e => e.Name is "." or "..");

        var file = Assert.Single(await sftp.ListAsync(dir.FullPath, default), e => e.Name == "hello.txt");
        Assert.False(file.IsDirectory);
        Assert.Equal(content.Length, file.Size);

        using var buffer = new MemoryStream();
        long reported = 0;
        await sftp.DownloadAsync(file.FullPath, buffer, new SyncProgress(n => reported = n), default);
        Assert.Equal(content, Encoding.UTF8.GetString(buffer.ToArray()));
        Assert.Equal(content.Length, reported);
    }

    [SkippableFact]
    public async Task Properties_and_rename_without_overwrite()
    {
        SshTestServer.Require();
        var trust = new MemoryTrustStore();
        var tag = Guid.NewGuid().ToString("N")[..8];
        await WriteRemoteFileAsync($"p-{tag}.txt", "12345", trust);
        await WriteRemoteFileAsync($"q-{tag}.txt", "other", trust);

        await using var sftp = new SshFileChannel(Entry(), Password, trust, new ScriptedPrompter(true));
        await sftp.ConnectAsync(default);
        var dir = $"{sftp.HomeDirectory}/sftp-live";

        var props = await sftp.GetPropertiesAsync($"{dir}/p-{tag}.txt", default);
        Assert.False(props.IsDirectory);
        Assert.Equal(5, props.Size);
        Assert.Equal(1000, props.UserId);
        Assert.NotEqual(0, props.Mode & 0x100); // owner can read

        await Assert.ThrowsAsync<IOException>(() => sftp.RenameAsync($"{dir}/p-{tag}.txt", $"{dir}/q-{tag}.txt", default));
        await sftp.RenameAsync($"{dir}/p-{tag}.txt", $"{dir}/r-{tag}.txt", default);

        var names = (await sftp.ListAsync(dir, default)).Select(e => e.Name).ToList();
        Assert.Contains($"r-{tag}.txt", names);
        Assert.Contains($"q-{tag}.txt", names);
        Assert.DoesNotContain($"p-{tag}.txt", names);
    }

    [SkippableFact]
    public async Task Wrong_password_is_an_authentication_failure()
    {
        SshTestServer.Require();
        await using var sftp = new SshFileChannel(Entry(), new SshCredentials("nope", null), new MemoryTrustStore(), new ScriptedPrompter(true));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => sftp.ConnectAsync(default));
    }

    [SkippableFact]
    public async Task Changed_host_key_is_a_hard_block()
    {
        SshTestServer.Require();
        var trust = new MemoryTrustStore();
        trust.Known[(SshTestServer.Host, SshTestServer.Port)] = "ssh-ed25519 SHA256:not-the-real-one";
        var prompter = new ScriptedPrompter(true);
        await using var sftp = new SshFileChannel(Entry(), Password, trust, prompter);

        await Assert.ThrowsAsync<HostKeyRejectedException>(() => sftp.ConnectAsync(default));
        Assert.Empty(prompter.Prompts);
    }

    private sealed class SyncProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
