using Shellboat.Core.Trust;

namespace Shellboat.Persistence.Tests;

public class JsonTrustStoreTests
{
    [Fact]
    public async Task Unknown_then_known_then_changed()
    {
        using var dir = new TempDir();
        var store = new JsonTrustStore(dir.File("known_hosts.json"));

        Assert.Equal(TrustVerdict.Unknown, await store.CheckAsync(TrustKind.SshHostKey, "Host.Example", 22, "fp1", default));
        await store.TrustAsync(TrustKind.SshHostKey, "Host.Example", 22, "fp1", default);

        Assert.Equal(TrustVerdict.Known, await store.CheckAsync(TrustKind.SshHostKey, "host.example", 22, "fp1", default));
        Assert.Equal(TrustVerdict.Changed, await store.CheckAsync(TrustKind.SshHostKey, "host.example", 22, "fp2", default));
        Assert.Equal(TrustVerdict.Unknown, await store.CheckAsync(TrustKind.SshHostKey, "host.example", 2222, "fp1", default));
    }

    [Fact]
    public async Task Trusting_again_replaces_the_old_fingerprint()
    {
        using var dir = new TempDir();
        var store = new JsonTrustStore(dir.File("known_hosts.json"));
        await store.TrustAsync(TrustKind.SshHostKey, "h", 22, "old", default);
        await store.TrustAsync(TrustKind.SshHostKey, "h", 22, "new", default);
        Assert.Equal(TrustVerdict.Known, await store.CheckAsync(TrustKind.SshHostKey, "h", 22, "new", default));
        Assert.Equal(TrustVerdict.Changed, await store.CheckAsync(TrustKind.SshHostKey, "h", 22, "old", default));
    }

    [Fact]
    public async Task Corrupt_trust_file_throws_rather_than_trusting_nothing_silently()
    {
        using var dir = new TempDir();
        var path = dir.File("known_hosts.json");
        await File.WriteAllTextAsync(path, "garbage");
        await Assert.ThrowsAsync<InvalidDataException>(() => new JsonTrustStore(path).CheckAsync(TrustKind.SshHostKey, "h", 22, "fp", default));
    }
}
