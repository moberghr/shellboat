using Shellboat.Core.Import;
using Shellboat.Persistence.Import;

namespace Shellboat.Persistence.Tests;

public class KeePassXcCliImporterTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <KeePassFile>
          <Meta><RecycleBinUUID>BIN</RecycleBinUUID></Meta>
          <Root>
            <Group>
              <UUID>ROOT</UUID><Name>Passwords</Name>
              <Entry>
                <String><Key>Title</Key><Value>top</Value></String>
                <String><Key>UserName</Key><Value>root</Value></String>
                <String><Key>Password</Key><Value>pw0</Value></String>
                <String><Key>URL</Key><Value>ssh://top.example</Value></String>
              </Entry>
              <Group>
                <UUID>G1</UUID><Name>Servers</Name>
                <Group>
                  <UUID>G2</UUID><Name>Prod</Name>
                  <Entry>
                    <String><Key>Title</Key><Value>web</Value></String>
                    <String><Key>Password</Key><Value>pw1</Value></String>
                    <String><Key>Notes</Key><Value>n</Value></String>
                  </Entry>
                </Group>
              </Group>
              <Group>
                <UUID>BIN</UUID><Name>Recycle Bin</Name>
                <Entry><String><Key>Title</Key><Value>deleted</Value></String></Entry>
              </Group>
            </Group>
          </Root>
        </KeePassFile>
        """;

    [Fact]
    public void Parses_entries_with_group_paths_and_skips_the_recycle_bin()
    {
        var entries = KeePassXcCliImporter.Parse(Xml);

        Assert.Equal(["top", "web"], entries.Select(e => e.Title));
        Assert.Empty(entries[0].GroupPath);
        Assert.Equal(["Servers", "Prod"], entries[1].GroupPath);
        Assert.Equal("pw1", entries[1].Password);
        Assert.Equal("n", entries[1].Notes);
    }

    [Fact]
    public async Task Master_password_goes_on_stdin_never_in_arguments()
    {
        var runner = new FakeCliRunner((_, _, _) => new CliResult(0, Xml, ""));
        var importer = new KeePassXcCliImporter(runner, () => "/bin/keepassxc-cli");

        var entries = await importer.ReadAsync("/db.kdbx", "s3cret", "/key.keyx", default);

        var call = Assert.Single(runner.Calls);
        Assert.Equal("/bin/keepassxc-cli", call.File);
        Assert.DoesNotContain(call.Args, a => a.Contains("s3cret"));
        Assert.Equal("s3cret\n", call.Stdin);
        Assert.Equal(["export", "--format", "xml", "--quiet", "--key-file", "/key.keyx", "/db.kdbx"], call.Args);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task Missing_tool_is_a_typed_failure()
    {
        var importer = new KeePassXcCliImporter(new FakeCliRunner((_, _, _) => default), () => null);
        var ex = await Assert.ThrowsAsync<CredentialImportException>(() => importer.ReadAsync("/db.kdbx", "x", null, default));
        Assert.Equal(ImportFailureKind.ToolNotFound, ex.Kind);
    }

    [Fact]
    public async Task Wrong_password_is_a_typed_failure_carrying_stderr_only()
    {
        var runner = new FakeCliRunner((_, _, _) => new CliResult(1, "", "Error while reading the database: Invalid credentials"));
        var importer = new KeePassXcCliImporter(runner, () => "keepassxc-cli");
        var ex = await Assert.ThrowsAsync<CredentialImportException>(() => importer.ReadAsync("/db.kdbx", "wrong", null, default));
        Assert.Equal(ImportFailureKind.WrongPasswordOrUnreadable, ex.Kind);
        Assert.Contains("Invalid credentials", ex.Message);
        Assert.DoesNotContain("wrong", ex.Message);
    }

    [Fact]
    public void Imported_entry_ToString_hides_the_password()
        => Assert.DoesNotContain("pw", new ImportedEntry("t", "u", "pw-secret", "", "", []).ToString());
}
