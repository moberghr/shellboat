using Shellboat.Core.Connections;
using Shellboat.Core.Import;
using Shellboat.Persistence.Import;

namespace Shellboat.Persistence.Tests;

/// <summary>Synthetic documents shaped like Royal TSX's .rtsz XML (element names as Royal writes them).</summary>
public class RoyalTsImporterTests
{
    private static readonly Guid Doc = Guid.NewGuid();
    private static readonly Guid Trash = Guid.NewGuid();
    private static readonly Guid Prod = Guid.NewGuid();
    private static readonly Guid Web = Guid.NewGuid();
    private static readonly Guid Cred = Guid.NewGuid();
    private static readonly Guid Ssh1 = Guid.NewGuid();
    private static readonly Guid Ssh2 = Guid.NewGuid();
    private static readonly Guid Rdp1 = Guid.NewGuid();

    private static string Document => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <RTSZDocument>
          <RoyalDocument><ID>{Doc}</ID><Name>Work</Name><DocumentType>Workspace</DocumentType></RoyalDocument>
          <RoyalTrash><ID>{Trash}</ID><ParentID>{Doc}</ParentID><Name>Trash</Name></RoyalTrash>
          <RoyalFolder><ID>{Prod}</ID><ParentID>{Doc}</ParentID><Name>Prod</Name><PositionNr>1</PositionNr></RoyalFolder>
          <RoyalFolder><ID>{Web}</ID><ParentID>{Prod}</ParentID><Name>Web</Name></RoyalFolder>
          <RoyalCredential><ID>{Cred}</ID><ParentID>{Doc}</ParentID><Name>deploy</Name><UserName>deploy</UserName><Password>ZW5jcnlwdGVk</Password></RoyalCredential>
          <RoyalSSHConnection>
            <ID>{Ssh1}</ID><ParentID>{Web}</ParentID><Name>web-1</Name><URI>10.0.0.11</URI><Port>2222</Port>
            <ConnectionType>ssh;SSH</ConnectionType><CredentialMode>2</CredentialMode><CredentialId>{Cred}</CredentialId>
            <Description>front end</Description>
          </RoyalSSHConnection>
          <RoyalSSHConnection>
            <ID>{Ssh2}</ID><ParentID>{Doc}</ParentID><Name>bastion</Name><URI>bastion.example</URI>
            <CredentialUsername>admin</CredentialUsername><CredentialPassword>ZW5jcnlwdGVk</CredentialPassword>
            <PrivateKeyPath>~/.ssh/id_ed25519</PrivateKeyPath>
          </RoyalSSHConnection>
          <RoyalRDSConnection><ID>{Rdp1}</ID><ParentID>{Prod}</ParentID><Name>dc</Name><URI>10.0.0.5</URI><RDPPort>3390</RDPPort><CredentialUsername>CORP\admin</CredentialUsername></RoyalRDSConnection>
          <RoyalRDSConnection><ID>{Guid.NewGuid()}</ID><ParentID>{Trash}</ParentID><Name>old</Name><URI>10.0.0.9</URI></RoyalRDSConnection>
          <RoyalSSHConnection><ID>{Guid.NewGuid()}</ID><ParentID>{Prod}</ParentID><Name>tpl</Name><URI>x</URI><IsConnectionTemplate>True</IsConnectionTemplate></RoyalSSHConnection>
          <RoyalSSHConnection><ID>{Guid.NewGuid()}</ID><ParentID>{Prod}</ParentID><Name>switch</Name><URI>10.0.0.1</URI><IsTelnetConnection>True</IsTelnetConnection></RoyalSSHConnection>
          <RoyalVNCConnection><ID>{Guid.NewGuid()}</ID><ParentID>{Prod}</ParentID><Name>vnc</Name><URI>10.0.0.2</URI></RoyalVNCConnection>
        </RTSZDocument>
        """;

    [Fact]
    public void Folders_keep_their_hierarchy_and_top_level_hangs_off_null()
    {
        var r = RoyalTsImporter.Parse(Document);
        Assert.Equal(2, r.Tree.Folders.Count);
        Assert.Null(r.Tree.Folders.Single(f => f.Id == Prod).ParentId);
        Assert.Equal(Prod, r.Tree.Folders.Single(f => f.Id == Web).ParentId);
    }

    [Fact]
    public void Ssh_connection_takes_its_user_from_the_linked_credential()
    {
        var c = RoyalTsImporter.Parse(Document).Tree.Connections.Single(c => c.Id == Ssh1);
        Assert.Equal((ConnectionKind.Ssh, "10.0.0.11", 2222, "deploy", Web), (c.Kind, c.Host, c.Port, c.UserName, c.FolderId));
        Assert.Equal("front end", c.Notes);
        Assert.Equal(SshAuthMethod.Password, c.Ssh!.Auth);
    }

    [Fact]
    public void Inline_user_and_key_path_and_top_level_placement()
    {
        var c = RoyalTsImporter.Parse(Document).Tree.Connections.Single(c => c.Id == Ssh2);
        Assert.Equal(("admin", 22, (Guid?)null), (c.UserName, c.EffectivePort, c.FolderId));
        Assert.Equal(new SshSettings(SshAuthMethod.PrivateKey, "~/.ssh/id_ed25519"), c.Ssh);
    }

    [Fact]
    public void Rdp_connection_with_custom_port_and_domain_user()
    {
        var c = RoyalTsImporter.Parse(Document).Tree.Connections.Single(c => c.Id == Rdp1);
        Assert.Equal((ConnectionKind.Rdp, 3390, "admin", "CORP"), (c.Kind, c.Port, c.UserName, c.Rdp!.Domain));
    }

    [Fact]
    public void Trash_templates_telnet_and_unsupported_types_are_skipped_and_counted()
    {
        var r = RoyalTsImporter.Parse(Document);
        Assert.Equal(3, r.Tree.Connections.Count);
        Assert.Equal(2, r.SkippedTemplatesAndTrash); // one template + one trashed connection
        Assert.Equal(2, r.SkippedUnsupported);           // telnet + VNC
    }

    [Fact]
    public void Encrypted_passwords_are_counted_never_imported()
    {
        var r = RoyalTsImporter.Parse(Document);
        Assert.Equal(2, r.EncryptedPasswordsNotImported);
        Assert.All(r.Tree.Connections, c => Assert.DoesNotContain("ZW5jcnlwdGVk", c.Notes + c.UserName + c.Host));
    }

    [Theory]
    [InlineData("10.0.0.1", "10.0.0.1", 0)]
    [InlineData("host:2200", "host", 2200)]
    [InlineData("ssh://host:2201", "host", 2201)]
    [InlineData("fe80::1", "fe80::1", 0)]
    public void Host_and_port_split(string uri, string host, int port)
        => Assert.Equal((host, port), RoyalTsImporter.SplitHostPort(uri));

    [Fact]
    public void Non_royal_or_encrypted_files_are_a_clear_failure()
    {
        Assert.Throws<CredentialImportException>(() => RoyalTsImporter.Parse("<KeePassFile/>"));
        Assert.Throws<CredentialImportException>(() => RoyalTsImporter.Parse("\u0001binary garbage"));
    }
}
