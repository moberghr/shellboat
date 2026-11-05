using System.Xml;
using System.Xml.Linq;
using Shellboat.Core.Import;

namespace Shellboat.Persistence.Import;

/// <summary>
/// Reads a KeePass database through <c>keepassxc-cli export --format xml</c>. KeePassXC does the KDBX
/// decryption, so we own no crypto and take no GPL dependency. The master password goes to the tool on
/// stdin — never as an argument, never logged — and the exported XML lives only in memory.
/// </summary>
public sealed class KeePassXcCliImporter(ICliRunner? runner = null, Func<string?>? locateTool = null) : ICredentialImporter
{
    private readonly ICliRunner _runner = runner ?? CliRunner.Instance;
    private readonly Func<string?> _locateTool = locateTool ?? LocateTool;

    public async Task<IReadOnlyList<ImportedEntry>> ReadAsync(string databasePath, string masterPassword, string? keyFilePath, CancellationToken ct)
    {
        var tool = _locateTool() ?? throw new CredentialImportException(ImportFailureKind.ToolNotFound,
            "keepassxc-cli was not found. Install KeePassXC (https://keepassxc.org) and try again.");

        List<string> args = ["export", "--format", "xml", "--quiet"];
        if (!string.IsNullOrEmpty(keyFilePath))
        {
            args.Add("--key-file");
            args.Add(keyFilePath);
        }

        args.Add(databasePath);

        CliResult r;
        try
        {
            // keepassxc-cli reads the password from stdin when stdin is not a terminal.
            r = await _runner.RunAsync(tool, args, masterPassword + "\n", ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException ex)
        {
            throw new CredentialImportException(ImportFailureKind.ToolNotFound, ex.Message);
        }

        if (r.Exit != 0)
        {
            throw new CredentialImportException(ImportFailureKind.WrongPasswordOrUnreadable,
                $"keepassxc-cli could not open the database: {r.Detail}");
        }

        return Parse(r.Stdout);
    }

    /// <summary>Parse KeePass 2 XML. Root group excluded from paths; the recycle bin is skipped.</summary>
    internal static IReadOnlyList<ImportedEntry> Parse(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new CredentialImportException(ImportFailureKind.UnexpectedOutput, $"keepassxc-cli returned unreadable XML: {ex.Message}");
        }

        var root = doc.Root?.Element("Root")?.Element("Group")
            ?? throw new CredentialImportException(ImportFailureKind.UnexpectedOutput, "The export has no root group.");
        var recycleBin = doc.Root?.Element("Meta")?.Element("RecycleBinUUID")?.Value;

        var result = new List<ImportedEntry>();
        Walk(root, [], result, recycleBin);
        return result;
    }

    private static void Walk(XElement group, IReadOnlyList<string> path, List<ImportedEntry> into, string? recycleBin)
    {
        foreach (var entry in group.Elements("Entry"))
        {
            string Field(string key) => entry.Elements("String")
                .FirstOrDefault(s => s.Element("Key")?.Value == key)?.Element("Value")?.Value ?? "";

            into.Add(new ImportedEntry(Field("Title"), Field("UserName"), Field("Password"), Field("URL"), Field("Notes"), path));
        }

        foreach (var child in group.Elements("Group"))
        {
            var uuid = child.Element("UUID")?.Value;
            if (!string.IsNullOrEmpty(recycleBin) && uuid == recycleBin)
            {
                continue;
            }

            Walk(child, [.. path, child.Element("Name")?.Value ?? "(unnamed)"], into, recycleBin);
        }
    }

    /// <summary>keepassxc-cli on PATH, else the copy inside the macOS app bundle / default Windows install.</summary>
    public static string? LocateTool()
    {
        var exe = OperatingSystem.IsWindows() ? "keepassxc-cli.exe" : "keepassxc-cli";
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir, exe);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string[] known = OperatingSystem.IsMacOS()
            ? ["/Applications/KeePassXC.app/Contents/MacOS/keepassxc-cli",
               Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications/KeePassXC.app/Contents/MacOS/keepassxc-cli")]
            : OperatingSystem.IsWindows()
                ? [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "KeePassXC", exe)]
                : ["/usr/bin/keepassxc-cli", "/snap/bin/keepassxc.cli"];
        return known.FirstOrDefault(File.Exists);
    }
}
