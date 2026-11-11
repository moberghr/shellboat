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
    public async Task Uploads_a_new_file_never_overwrites_and_removes_a_failed_one()
    {
        SshTestServer.Require();
        var trust = new MemoryTrustStore();
        var tag = Guid.NewGuid().ToString("N")[..8];
        await WriteRemoteFileAsync($"keep-{tag}.txt", "original", trust);

        await using var sftp = new SshFileChannel(Entry(), Password, trust, new ScriptedPrompter(true));
        await sftp.ConnectAsync(default);
        var dir = $"{sftp.HomeDirectory}/sftp-live/up-{tag}";

        Assert.False(await sftp.ExistsAsync(dir, default));
        await sftp.CreateDirectoryAsync(dir, default);
        Assert.True(await sftp.ExistsAsync(dir, default));
        await Assert.ThrowsAnyAsync<Exception>(() => sftp.CreateDirectoryAsync(dir, default));

        var content = Encoding.UTF8.GetBytes("uploaded " + new string('x', 100_000));
        long reported = 0;
        await sftp.UploadAsync(new MemoryStream(content), $"{dir}/new.txt", new SyncProgress(n => reported = n), default);
        using (var back = new MemoryStream())
        {
            await sftp.DownloadAsync($"{dir}/new.txt", back, null, default);
            Assert.Equal(content, back.ToArray());
        }

        Assert.Equal(content.Length, reported);

        // An existing file is refused and left exactly as it was.
        var keep = $"{sftp.HomeDirectory}/sftp-live/keep-{tag}.txt";
        await Assert.ThrowsAnyAsync<Exception>(() => sftp.UploadAsync(new MemoryStream("evil"u8.ToArray()), keep, null, default));
        using (var back = new MemoryStream())
        {
            await sftp.DownloadAsync(keep, back, null, default);
            Assert.Equal("original", Encoding.UTF8.GetString(back.ToArray()));
        }

        // A source that breaks half-way leaves nothing behind.
        await Assert.ThrowsAsync<IOException>(() => sftp.UploadAsync(new BreakingStream(64 * 1024), $"{dir}/broken.bin", null, default));
        Assert.False(await sftp.ExistsAsync($"{dir}/broken.bin", default));
    }

    /// <summary>Reads <c>goodBytes</c> zeros, then throws — a local disk/network failure mid-upload.</summary>
    private sealed class BreakingStream(int goodBytes) : Stream
    {
        private int _left = goodBytes;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_left <= 0)
            {
                throw new IOException("local read failed");
            }

            var n = Math.Min(count, _left);
            Array.Clear(buffer, offset, n);
            _left -= n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
