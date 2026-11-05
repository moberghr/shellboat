using Shellboat.App.Services;
using Shellboat.App.ViewModels;
using Shellboat.Core.Connections;
using Shellboat.Core.Import;
using Shellboat.Core.Secrets;

namespace Shellboat.App.Tests;

public class CopyPasswordTests
{
    private sealed class NoImporter : ICredentialImporter
    {
        public Task<IReadOnlyList<ImportedEntry>> ReadAsync(string databasePath, string masterPassword, string? keyFilePath, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private static async Task<(MainWindowViewModel Vm, FakeSecrets Secrets, FakeDialogs Dialogs, ConnectionEntry Conn)> CreateAsync(TimeSpan? lifetime = null)
    {
        var store = new MemoryConnectionStore();
        var secrets = new FakeSecrets();
        var tree = new ConnectionTreeViewModel(store, secrets);
        var conn = new ConnectionEntry { Id = Guid.NewGuid(), Name = "web", Kind = ConnectionKind.Ssh, Host = "web", Ssh = new SshSettings() };
        await tree.UpsertAsync(conn);
        var dialogs = new FakeDialogs();
        var vm = new MainWindowViewModel(tree, secrets, dialogs,
            (_, _) => throw new NotSupportedException(), (_, _) => throw new NotSupportedException(), new NoImporter())
        {
            PasswordClipboardLifetime = lifetime ?? TimeSpan.Zero,
        };
        return (vm, secrets, dialogs, conn);
    }

    [Fact]
    public async Task Copies_the_saved_password_and_keeps_it_out_of_the_status_bar()
    {
        var (vm, secrets, dialogs, conn) = await CreateAsync(TimeSpan.FromMinutes(5));
        secrets.Items[(conn.Id, SecretPurpose.Password)] = "s3cret!";

        await vm.CopyPasswordCommand.ExecuteAsync(vm.Tree.Roots.Single());

        Assert.Equal("s3cret!", dialogs.Clipboard);
        Assert.DoesNotContain("s3cret!", vm.StatusText);
        Assert.Contains("web", vm.StatusText);
    }

    [Fact]
    public async Task The_copied_password_is_wiped_from_the_clipboard_after_the_lifetime()
    {
        var (vm, secrets, dialogs, conn) = await CreateAsync();
        secrets.Items[(conn.Id, SecretPurpose.Password)] = "s3cret!";

        await vm.CopyPasswordCommand.ExecuteAsync(vm.Tree.Roots.Single());
        await vm.PendingClipboardClear;

        Assert.Null(dialogs.Clipboard);
    }

    [Fact]
    public async Task The_wipe_leaves_alone_whatever_the_user_copied_since()
    {
        var (vm, secrets, dialogs, conn) = await CreateAsync();
        secrets.Items[(conn.Id, SecretPurpose.Password)] = "s3cret!";

        await vm.CopyPasswordCommand.ExecuteAsync(vm.Tree.Roots.Single());
        dialogs.UserCopies("ls -la");
        await vm.PendingClipboardClear;

        Assert.Equal("ls -la", dialogs.Clipboard);
    }

    [Fact]
    public async Task Without_a_saved_password_it_asks_and_cancelling_copies_nothing()
    {
        var (vm, _, dialogs, _) = await CreateAsync();

        await vm.CopyPasswordCommand.ExecuteAsync(vm.Tree.Roots.Single());

        Assert.Null(dialogs.Clipboard);
        Assert.Equal("No password copied for \"web\".", vm.StatusText);
    }

    [Fact]
    public async Task Without_a_saved_password_the_typed_one_is_copied_and_remembered_for_next_time()
    {
        var (vm, secrets, dialogs, conn) = await CreateAsync(TimeSpan.FromMinutes(5));
        dialogs.SecretAnswers.Enqueue(new SecretPromptResult("sudo-pw", Remember: true));

        await vm.CopyPasswordCommand.ExecuteAsync(vm.Tree.Roots.Single());

        Assert.Equal("sudo-pw", dialogs.Clipboard);
        Assert.Equal("sudo-pw", secrets.Items[(conn.Id, SecretPurpose.Password)]);
    }

    [Fact]
    public async Task On_a_key_auth_connection_the_saved_password_does_not_touch_the_key_passphrase()
    {
        var (vm, secrets, dialogs, conn) = await CreateAsync(TimeSpan.FromMinutes(5));
        await vm.Tree.UpsertAsync(conn with { Ssh = new SshSettings { Auth = SshAuthMethod.PrivateKey, PrivateKeyPath = "~/.ssh/id" } });
        dialogs.SecretAnswers.Enqueue(new SecretPromptResult("sudo-pw", Remember: true));

        await vm.CopyPasswordCommand.ExecuteAsync(vm.Tree.Roots.Single());

        Assert.Equal("sudo-pw", dialogs.Clipboard);
        Assert.False(secrets.Items.ContainsKey((conn.Id, SecretPurpose.KeyPassphrase)));
    }
}
