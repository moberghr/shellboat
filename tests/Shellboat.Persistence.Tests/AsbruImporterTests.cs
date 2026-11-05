using Shellboat.Core.Connections;
using Shellboat.Core.Import;
using Shellboat.Core.Secrets;
using Shellboat.Persistence.Import;

namespace Shellboat.Persistence.Tests;

/// <summary>Synthetic exports shaped like Ásbrú's asbru_export.yml (Perl YAML, keys as Ásbrú writes them).</summary>
public class AsbruImporterTests
{
    private static readonly Guid Prod = Guid.NewGuid();
    private static readonly Guid Web = Guid.NewGuid();
    private static readonly Guid Ssh1 = Guid.NewGuid();
    private static readonly Guid SshKey = Guid.NewGuid();
    private static readonly Guid SshManual = Guid.NewGuid();
    private static readonly Guid SshEnc = Guid.NewGuid();
    private static readonly Guid Rdp1 = Guid.NewGuid();
    private static readonly Guid Rdp2 = Guid.NewGuid();
    private static readonly Guid Vnc = Guid.NewGuid();

    private const string Encrypted = "53616c7465645f5f0123456789abcdef0123456789abcdef";

    private static string Connection(Guid id, Guid parent, string method, string name, string ip, string port,
        string user = "u", string pass = "''", string auth = "userpass", string key = "''", string passphrase = "''",
        string options = "''", string description = "", string jump = "''") => $"""
        {id}:
          _is_group: 0
          _protected: 0
          auth type: {auth}
          children: {"{}"}
          description: {(description.Length == 0 ? $"Connection with '{name}-old'" : description)}
          ip: {ip}
          jump ip: {jump}
          method: {method}
          name: {name}
          options: {options}
          parent: {parent}
          pass: {pass}
          passphrase: {passphrase}
          port: {port}
          public key: {key}
          title: {name}
          user: {user}

        """;

    private static string Group(Guid id, string parent, string name, params Guid[] children) => $"""
        {id}:
          _is_group: 1
          _protected: 0
          children:
        {string.Concat(children.Select(c => $"    {c}: 1\n"))}  description: Connection group '{name}'
          name: {name}
          parent: {parent}

        """;

    private static string Export => "---\n"
        + Connection(Ssh1, Web, "SSH", "web-1", "10.0.0.11", "2222", pass: "s3cret:with 'quotes'", description: "front end")
        + Connection(SshKey, Prod, "SSH", "bastion", "bastion.example", "22", user: "admin", auth: "publickey", key: "/home/me/.ssh/id_ed25519", passphrase: "keypass", pass: "ignored")
        + Connection(SshManual, Prod, "SSH", "manual", "m.example", "22", auth: "manual", pass: "ignored", jump: "jump.example")
        + Connection(SshEnc, Prod, "SSH", "enc", "e.example", "22", pass: Encrypted)
        + Connection(Rdp1, Prod, "RDP (xfreerdp)", "dc", "10.0.0.5", "3390", user: "CORP\\admin", pass: "rdp-pw", options: "' /bpp:24 /cert-ignore'")
        + Connection(Rdp2, Web, "RDP (rdesktop)", "term", "10.0.0.6", "3389", user: "bob", options: "' /d:SALES /bpp:16'")
        + Connection(Vnc, Prod, "VNC", "screen", "10.0.0.7", "5900")
        + Group(Prod, "__PAC__EXPORTED__", "Prod", Web, SshKey)
        + Group(Web, Prod.ToString(), "Web", Ssh1, Rdp2)
        + """
        __PAC__EXPORTED__:
          children:
            {PROD}: 1
          name: Export

        """.Replace("{PROD}", Prod.ToString());

    [Fact]
    public void Groups_become_nested_folders_with_asbru_ids()
    {
        var result = AsbruImporter.Parse(Export);

        var prod = Assert.Single(result.Tree.Folders, f => f.Id == Prod);
        Assert.Equal("Prod", prod.Name);
        Assert.Null(prod.ParentId);
        Assert.Equal(Prod, Assert.Single(result.Tree.Folders, f => f.Id == Web).ParentId);
    }

    [Fact]
    public void Ssh_and_rdp_connections_map_host_port_user_folder()
    {
        var result = AsbruImporter.Parse(Export);
        var web = result.Tree.Connections.Single(c => c.Id == Ssh1);

        Assert.Equal((ConnectionKind.Ssh, "web-1", "10.0.0.11", 2222, "u", Web), (web.Kind, web.Name, web.Host, web.Port, web.UserName, web.FolderId));
        Assert.Equal(SshAuthMethod.Password, web.Ssh!.Auth);
        Assert.Equal("front end", web.Notes);

        var dc = result.Tree.Connections.Single(c => c.Id == Rdp1);
        Assert.Equal((ConnectionKind.Rdp, 3390, "admin", "CORP"), (dc.Kind, dc.Port, dc.UserName, dc.Rdp!.Domain));

        var term = result.Tree.Connections.Single(c => c.Id == Rdp2);
        Assert.Equal(("bob", "SALES"), (term.UserName, term.Rdp!.Domain));
    }

    [Fact]
    public void Default_description_is_dropped()
    {
        var result = AsbruImporter.Parse(Export);
        Assert.Equal("", result.Tree.Connections.Single(c => c.Id == SshKey).Notes);
    }

    [Fact]
    public void Public_key_auth_uses_the_key_file_and_its_passphrase()
    {
        var result = AsbruImporter.Parse(Export);
        var bastion = result.Tree.Connections.Single(c => c.Id == SshKey);

        Assert.Equal(new SshSettings(SshAuthMethod.PrivateKey, "/home/me/.ssh/id_ed25519"), bastion.Ssh);
        var secret = Assert.Single(result.Secrets, s => s.ConnectionId == SshKey);
        Assert.Equal((SecretPurpose.KeyPassphrase, "keypass"), (secret.Purpose, secret.Value));
    }

    [Fact]
    public void Plain_passwords_are_returned_as_secrets_exactly_as_written()
    {
        var result = AsbruImporter.Parse(Export);

        Assert.Equal("s3cret:with 'quotes'", result.Secrets.Single(s => s.ConnectionId == Ssh1).Value);
        Assert.Equal("rdp-pw", result.Secrets.Single(s => s.ConnectionId == Rdp1).Value);
        Assert.DoesNotContain(result.Secrets, s => s.ConnectionId == SshManual); // manual = asked at connect
        Assert.Equal(3, result.Secrets.Count);
    }

    [Fact]
    public void Encrypted_passwords_are_counted_not_decrypted()
    {
        var result = AsbruImporter.Parse(Export);

        Assert.Equal(1, result.EncryptedPasswordsNotImported);
        Assert.DoesNotContain(result.Secrets, s => s.ConnectionId == SshEnc);
        Assert.Contains(result.Tree.Connections, c => c.Id == SshEnc);
    }

    [Fact]
    public void Unsupported_jump_and_cert_ignore_are_reported()
    {
        var result = AsbruImporter.Parse(Export);

        Assert.Equal(1, result.SkippedUnsupported);
        Assert.DoesNotContain(result.Tree.Connections, c => c.Id == Vnc);
        Assert.Equal(1, result.JumpHostsIgnored);
        Assert.Equal(1, result.CertificateChecksDropped);
    }

    [Fact]
    public void Config_file_layout_with_environments_is_read_too()
    {
        var indented = string.Join("\n", Export.Split('\n').Skip(1).Select(l => l.Length == 0 ? l : "  " + l));
        var config = "---\nconfig:\n  version: 6.4\nenvironments:\n" + indented;

        var result = AsbruImporter.Parse(config);

        Assert.Equal(6, result.Tree.Connections.Count);
        Assert.Equal(2, result.Tree.Folders.Count);
    }

    [Fact]
    public void Secrets_never_appear_in_ToString()
    {
        var result = AsbruImporter.Parse(Export);

        Assert.DoesNotContain("s3cret", result.ToString());
        Assert.All(result.Secrets, s => Assert.DoesNotContain(s.Value, s.ToString()));
    }

    [Theory]
    [InlineData("abc:\n  pass: [hunter2\n  x: y")]
    [InlineData("abc:\n  pass: \"hunter2\n")]
    [InlineData("abc:\n\tpass: hunter2")]
    public void Broken_yaml_is_an_import_error_that_never_quotes_content(string yaml)
    {
        var ex = Assert.Throws<CredentialImportException>(() => AsbruImporter.Parse(yaml));

        Assert.StartsWith("This is not a readable Ásbrú export", ex.Message);
        Assert.DoesNotContain("hunter2", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("just a string")]
    [InlineData("name: not asbru\nversion: 3")]
    public void Files_that_are_not_asbru_exports_are_rejected(string yaml)
        => Assert.Throws<CredentialImportException>(() => AsbruImporter.Parse(yaml));

    [Theory]
    [InlineData(Encrypted, true)]
    [InlineData("53616c7465645f5f", false)]
    [InlineData("plainpassword123456789012345678901", false)]
    public void Encrypted_detection(string value, bool encrypted)
        => Assert.Equal(encrypted, AsbruImporter.LooksEncrypted(value));
}
