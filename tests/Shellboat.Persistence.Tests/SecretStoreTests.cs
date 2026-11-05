using Shellboat.Core.Secrets;
using Shellboat.Persistence.Secrets;

namespace Shellboat.Persistence.Tests;

public class SecretStoreTests
{
    [Fact]
    public async Task Probe_accepts_a_working_store_and_cleans_up()
    {
        var store = new FakeSecretStore();
        Assert.Null(await SecretStoreFactory.ProbeFailureAsync(store, default));
        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task Probe_rejects_a_store_that_reads_back_wrong()
    {
        var store = new FakeSecretStore { ReadTransform = _ => "other" };
        Assert.NotNull(await SecretStoreFactory.ProbeFailureAsync(store, default));
        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task No_secret_store_refuses_writes_and_holds_nothing()
    {
        var store = new NoSecretStore("no keychain");
        Assert.False(store.CanStore);
        await Assert.ThrowsAsync<SecretStorageRefusedException>(() => store.SetAsync(Guid.NewGuid(), SecretPurpose.Password, "x", default));
        Assert.Null(await store.GetAsync(Guid.NewGuid(), SecretPurpose.Password, default));
    }

    [Fact]
    public async Task Secret_tool_store_passes_the_secret_on_stdin_and_keys_purposes_apart()
    {
        var stored = new Dictionary<string, string>();
        var runner = new FakeCliRunner((_, args, stdin) =>
        {
            var account = args[^1];
            switch (args[0])
            {
                case "store":
                    stored[account] = stdin!;
                    return new CliResult(0, "", "");
                case "lookup":
                    return stored.TryGetValue(account, out var v) ? new CliResult(0, v + "\n", "") : new CliResult(1, "", "");
                default:
                    return new CliResult(0, "", "");
            }
        });
        var store = new SecretToolSecretStore(runner, "shellboat-test");
        var id = Guid.NewGuid();

        await store.SetAsync(id, SecretPurpose.Password, "pw", default);
        await store.SetAsync(id, SecretPurpose.KeyPassphrase, "pass", default);

        Assert.Equal("pw", await store.GetAsync(id, SecretPurpose.Password, default));
        Assert.Equal("pass", await store.GetAsync(id, SecretPurpose.KeyPassphrase, default));
        Assert.All(runner.Calls, c => Assert.DoesNotContain(c.Args, a => a is "pw" or "pass"));
    }

    [Fact]
    public async Task Secret_tool_lookup_failure_with_stderr_is_raised_not_read_as_missing()
    {
        var runner = new FakeCliRunner((_, _, _) => new CliResult(1, "", "The secret was transferred or encrypted in an invalid way"));
        var store = new SecretToolSecretStore(runner, "shellboat-test");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetAsync(Guid.NewGuid(), SecretPurpose.Password, default));
        Assert.Equal(3, runner.Calls.Count);
    }

    [Fact]
    public void Cli_result_never_prints_stdout()
        => Assert.DoesNotContain("hunter2", new CliResult(0, "hunter2", "").ToString());
}
