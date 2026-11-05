using System.Xml;
using System.Xml.Linq;
using Shellboat.Core.Connections;
using Shellboat.Core.Import;

namespace Shellboat.Persistence.Import;

/// <summary>What a Royal TS/TSX document import produced, and what it left out (so the UI can say so).</summary>
/// <param name="Tree">Folders and connections with Royal's own ids, so importing the same document again
/// updates rather than duplicates. Top-level folders/connections have a null parent.</param>
public sealed record RoyalImportResult(
    ConnectionTreeDocument Tree,
    int SkippedUnsupported,
    int SkippedTemplatesAndTrash,
    int EncryptedPasswordsNotImported);

/// <summary>
/// Reads a Royal TS / Royal TSX document (<c>.rtsz</c>, plain XML). Imports folders, SSH and RDP connections,
/// hosts, ports, usernames (inline or from a linked credential object) and key-file paths.
/// <para>
/// <b>Passwords are not imported.</b> Royal stores them encrypted with an undocumented scheme; Shellboat does
/// not try to break it. They are counted in <see cref="RoyalImportResult.EncryptedPasswordsNotImported"/> so the
/// user knows to enter them (or import them from KeePass).
/// </para>
/// </summary>
public static class RoyalTsImporter
{
    public static async Task<RoyalImportResult> ReadFileAsync(string path, CancellationToken ct)
    {
        var xml = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return Parse(xml);
    }

    public static RoyalImportResult Parse(string xml)
    {
        XElement root;
        try
        {
            root = XDocument.Parse(xml).Root ?? throw new XmlException("empty document");
        }
        catch (XmlException ex)
        {
            throw new CredentialImportException(ImportFailureKind.UnexpectedOutput,
                $"This is not a readable Royal document (it may be password-protected — export it unencrypted first): {ex.Message}");
        }

        if (root.Name.LocalName != "RTSZDocument")
        {
            throw new CredentialImportException(ImportFailureKind.UnexpectedOutput, "This file is not a Royal TS/TSX document.");
        }

        var objects = root.Elements().Select(e => new RoyalObject(e)).Where(o => o.Id is not null).ToList();
        var documentIds = objects.Where(o => o.Type == "RoyalDocument").Select(o => o.Id!.Value).ToHashSet();
        var trashIds = objects.Where(o => o.Type == "RoyalTrash").Select(o => o.Id!.Value).ToHashSet();
        var byId = objects.ToDictionary(o => o.Id!.Value);
        var credentials = objects.Where(o => o.Type == "RoyalCredential").ToList();

        bool InTrash(RoyalObject o)
        {
            var seen = new HashSet<Guid>();
            for (var parent = o.ParentId; parent is { } p && seen.Add(p); parent = byId.TryGetValue(p, out var po) ? po.ParentId : null)
            {
                if (trashIds.Contains(p))
                {
                    return true;
                }
            }

            return false;
        }

        // Anything parented to the document itself (or to something we don't import) is top level.
        Guid? Parent(RoyalObject o, HashSet<Guid> folderIds)
            => o.ParentId is { } p && !documentIds.Contains(p) && folderIds.Contains(p) ? p : null;

        var folderObjects = objects.Where(o => o.Type == "RoyalFolder" && !InTrash(o)).ToList();
        var folderIds = folderObjects.Select(o => o.Id!.Value).ToHashSet();
        var folders = folderObjects
            .Select(o => new Folder(o.Id!.Value, o.Text("Name") ?? "Folder", Parent(o, folderIds), o.Int("PositionNr")))
            .ToList();

        var connections = new List<ConnectionEntry>();
        int unsupported = 0, templatesAndTrash = 0, encrypted = 0;
        foreach (var o in objects)
        {
            var kind = o.Type switch
            {
                "RoyalSSHConnection" => ConnectionKind.Ssh,
                "RoyalRDSConnection" => ConnectionKind.Rdp,
                _ => (ConnectionKind?)null,
            };

            if (kind is null)
            {
                if (o.Type.EndsWith("Connection", StringComparison.Ordinal))
                {
                    unsupported++; // VNC, web, file transfer, … — not in Shellboat yet
                }

                continue;
            }

            if (o.Bool("IsConnectionTemplate") || InTrash(o))
            {
                templatesAndTrash++;
                continue;
            }

            if (o.Bool("IsTelnetConnection") || o.Bool("IsSerialPortConnection"))
            {
                unsupported++;
                continue;
            }

            var (host, uriPort) = SplitHostPort(o.Text("URI") ?? "");
            if (host.Length == 0)
            {
                unsupported++;
                continue;
            }

            var port = o.Int(kind == ConnectionKind.Rdp ? "RDPPort" : "Port");
            if (port == 0)
            {
                port = uriPort;
            }

            var linked = Linked(o, credentials);
            var user = o.Text("CredentialUsername") is { Length: > 0 } inline ? inline : linked?.Text("UserName") ?? "";
            if (o.HasValue("CredentialPassword") || o.HasValue("CredentialPassphrase") || linked?.HasValue("Password") == true)
            {
                encrypted++;
            }

            var keyPath = o.Text("PrivateKeyPath") is { Length: > 0 } k ? k : o.Text("CredentialKeyFile");
            var domain = "";
            if (kind == ConnectionKind.Rdp && user.Contains('\\'))
            {
                domain = user[..user.IndexOf('\\')];
                user = user[(user.IndexOf('\\') + 1)..];
            }

            connections.Add(new ConnectionEntry
            {
                Id = o.Id!.Value,
                Name = o.Text("Name") is { Length: > 0 } n ? n : host,
                FolderId = Parent(o, folderIds),
                SortOrder = o.Int("PositionNr"),
                Kind = kind.Value,
                Host = host,
                Port = port,
                UserName = user,
                Notes = o.Text("Description") ?? "",
                Ssh = kind == ConnectionKind.Ssh
                    ? new SshSettings(string.IsNullOrEmpty(keyPath) ? SshAuthMethod.Password : SshAuthMethod.PrivateKey, string.IsNullOrEmpty(keyPath) ? null : keyPath)
                    : null,
                Rdp = kind == ConnectionKind.Rdp ? new RdpSettings(Domain: domain) : null,
            });
        }

        return new RoyalImportResult(new ConnectionTreeDocument(folders, connections), unsupported, templatesAndTrash, encrypted);
    }

    /// <summary>The credential object a connection points at, by id first and then by name.</summary>
    private static RoyalObject? Linked(RoyalObject o, List<RoyalObject> credentials)
    {
        if (o.GuidOf("CredentialId") is { } id && credentials.FirstOrDefault(c => c.Id == id) is { } byId)
        {
            return byId;
        }

        return o.Text("CredentialName") is { Length: > 0 } name
            ? credentials.FirstOrDefault(c => string.Equals(c.Text("Name"), name, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    internal static (string Host, int Port) SplitHostPort(string uri)
    {
        var value = uri.Trim();
        if (value.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(value, UriKind.Absolute, out var u))
        {
            return (u.Host, u.IsDefaultPort || u.Port < 0 ? 0 : u.Port);
        }

        // host:port, but not a bare IPv6 address.
        if (value.Count(c => c == ':') == 1 && int.TryParse(value[(value.IndexOf(':') + 1)..], out var port))
        {
            return (value[..value.IndexOf(':')], port);
        }

        return (value.Trim('[', ']'), 0);
    }

    private sealed class RoyalObject(XElement element)
    {
        public string Type { get; } = element.Name.LocalName;

        public Guid? Id { get; } = System.Guid.TryParse(element.Element("ID")?.Value, out var id) ? id : null;

        public Guid? ParentId { get; } = System.Guid.TryParse(element.Element("ParentID")?.Value, out var p) ? p : null;

        public string? Text(string name) => element.Element(name)?.Value.Trim();

        public bool HasValue(string name) => !string.IsNullOrWhiteSpace(element.Element(name)?.Value);

        public bool Bool(string name) => bool.TryParse(element.Element(name)?.Value, out var b) && b;

        public int Int(string name) => int.TryParse(element.Element(name)?.Value, out var i) ? i : 0;

        public Guid? GuidOf(string name) => System.Guid.TryParse(element.Element(name)?.Value, out var g) ? g : null;
    }
}
