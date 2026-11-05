using Shellboat.App.Services;
using Shellboat.App.ViewModels;
using Shellboat.Core.Connections;
using Shellboat.Core.Secrets;
using Shellboat.Persistence.Import;

namespace Shellboat.App.Tests;

public class AsbruImportApplierTests
{
    private static readonly Guid Group = Guid.NewGuid();
    private static readonly Guid Ssh = Guid.NewGuid();
    private static readonly Guid Rdp = Guid.NewGuid();

    private static AsbruImportResult Result(string host = "h1", int certIgnore = 0) => new(
        new ConnectionTreeDocument(
            [new Folder(Group, "Prod", null)],
            [
                new ConnectionEntry { Id = Ssh, Name = "web", FolderId = Group, Kind = ConnectionKind.Ssh, Host = host, Ssh = new SshSettings() },
                new ConnectionEntry { Id = Rdp, Name = "dc", Kind = ConnectionKind.Rdp, Host = "dc", Rdp = new RdpSettings() },
            ]),
        [new ImportedSecret(Ssh, SecretPurpose.Password, "pw1"), new ImportedSecret(Rdp, SecretPurpose.Password, "pw2")],
        SkippedUnsupported: 1,
        EncryptedPasswordsNotImported: 0,
        JumpHostsIgnored: 0,
        CertificateChecksDropped: certIgnore);

    [Fact]
    public async Task Imports_under_one_root_folder_and_saves_passwords_to_the_keychain()
    {
        var store = new MemoryConnectionStore();
        var tree = new ConnectionTreeViewModel(store, new FakeSecrets());
        var secrets = new FakeSecrets();

        var outcome = await AsbruImportApplier.ApplyAsync(tree, secrets, Result(certIgnore: 2), CancellationToken.None);

        var root = Assert.Single(store.Saved.Folders, f => f.Name == AsbruImportApplier.RootFolderName);
        Assert.Equal(root.Id, store.Saved.Folders.Single(f => f.Id == Group).ParentId);
        Assert.Equal(root.Id, store.Saved.Connections.Single(c => c.Id == Rdp).FolderId);
        Assert.Equal("pw1", secrets.Items[(Ssh, SecretPurpose.Password)]);
        Assert.Equal("pw2", secrets.Items[(Rdp, SecretPurpose.Password)]);
        Assert.Equal((2, 0, 2, 0), (outcome.Added, outcome.Updated, outcome.SecretsSaved, outcome.SecretsDropped));
        Assert.Contains(outcome.Notes, n => n.Contains("2 RDP connection(s) had certificate checks turned off"));
        Assert.Contains(outcome.Notes, n => n.Contains("delete it"));
        Assert.DoesNotContain(outcome.Notes, n => n.Contains("pw1") || n.Contains("pw2"));
    }

    [Fact]
    public async Task Reimport_updates_instead_of_duplicating()
    {
        var store = new MemoryConnectionStore();
        var tree = new ConnectionTreeViewModel(store, new FakeSecrets());

        await AsbruImportApplier.ApplyAsync(tree, new FakeSecrets(), Result(), CancellationToken.None);
        var outcome = await AsbruImportApplier.ApplyAsync(tree, new FakeSecrets(), Result(host: "h2"), CancellationToken.None);

        Assert.Equal((0, 2), (outcome.Added, outcome.Updated));
        Assert.Equal(2, store.Saved.Connections.Count);
        Assert.Equal("h2", store.Saved.Connections.Single(c => c.Id == Ssh).Host);
        Assert.Single(store.Saved.Folders, f => f.Name == AsbruImportApplier.RootFolderName);
    }

    [Fact]
    public async Task Without_a_keychain_passwords_are_dropped_and_the_user_is_told()
    {
        var store = new MemoryConnectionStore();
        var tree = new ConnectionTreeViewModel(store, new FakeSecrets());
        var secrets = new FakeSecrets(canStore: false);

        var outcome = await AsbruImportApplier.ApplyAsync(tree, secrets, Result(), CancellationToken.None);

        Assert.Empty(secrets.Items);
        Assert.Equal((0, 2), (outcome.SecretsSaved, outcome.SecretsDropped));
        Assert.Contains(outcome.Notes, n => n.Contains("no system keychain"));
        Assert.Equal(2, store.Saved.Connections.Count);
    }
}
