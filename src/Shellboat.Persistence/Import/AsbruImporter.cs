using System.Text.RegularExpressions;
using Shellboat.Core.Connections;
using Shellboat.Core.Import;
using Shellboat.Core.Secrets;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Shellboat.Persistence.Import;

/// <summary>A secret read from an import file, headed for the keychain. Never printed.</summary>
public sealed record ImportedSecret(Guid ConnectionId, SecretPurpose Purpose, string Value)
{
    public override string ToString() => $"{nameof(ImportedSecret)} {{ ConnectionId = {ConnectionId}, Purpose = {Purpose} }}";
}

/// <summary>What an Ásbrú import produced, and what it left out (so the UI can say so).</summary>
/// <param name="Tree">Groups → folders, SSH/RDP → connections, with Ásbrú's own UUIDs so re-importing updates.</param>
/// <param name="Secrets">Plain-text passwords / key passphrases from the export. The caller stores them in the
/// keychain or drops them; they are never written anywhere else.</param>
/// <param name="CertificateChecksDropped">RDP connections that had <c>/cert-ignore</c>; Shellboat always verifies.</param>
public sealed record AsbruImportResult(
    ConnectionTreeDocument Tree,
    IReadOnlyList<ImportedSecret> Secrets,
    int SkippedUnsupported,
    int EncryptedPasswordsNotImported,
    int JumpHostsIgnored,
    int CertificateChecksDropped)
{
    public override string ToString()
        => $"{nameof(AsbruImportResult)} {{ Folders = {Tree.Folders.Count}, Connections = {Tree.Connections.Count}, Secrets = {Secrets.Count} }}";
}

/// <summary>
/// Reads an Ásbrú Connection Manager export (<c>asbru_export.yml</c>) or its config file (<c>asbru.yml</c>, entries
/// under <c>environments</c>). Every entry is keyed by UUID; <c>_is_group: 1</c> marks a group, <c>parent</c> links
/// the tree, and the root is <c>__PAC__EXPORTED__</c> / <c>__PAC__ROOT__</c>.
/// <para>
/// Exports carry passwords in plain text; those are returned in <see cref="AsbruImportResult.Secrets"/>. Ásbrú's
/// config file stores them encrypted (hex of an OpenSSL "Salted__" blob) — those are counted, <b>never decrypted</b>.
/// </para>
/// </summary>
public static partial class AsbruImporter
{
    private static readonly string[] RootKeys = ["__PAC__EXPORTED__", "__PAC__ROOT__"];

    public static async Task<AsbruImportResult> ReadFileAsync(string path, CancellationToken ct)
    {
        var yaml = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return Parse(yaml);
    }

    public static AsbruImportResult Parse(string yaml)
    {
        var entries = LoadEntries(yaml);
        var groupIds = entries.Where(e => e.Value.IsGroup).Select(e => e.Key).ToHashSet();
        Guid? Parent(Entry e) => e.ParentId is { } p && groupIds.Contains(p) ? p : null;

        var folders = entries
            .Where(e => e.Value.IsGroup)
            .Select(e => new Folder(e.Key, e.Value.Text("name") is { Length: > 0 } n ? n : "Group", Parent(e.Value)))
            .ToList();

        var connections = new List<ConnectionEntry>();
        var secrets = new List<ImportedSecret>();
        int unsupported = 0, encrypted = 0, jumps = 0, certIgnore = 0;
        foreach (var (id, e) in entries)
        {
            if (e.IsGroup)
            {
                continue;
            }

            var method = e.Text("method") ?? "";
            ConnectionKind? kind = method == "SSH" ? ConnectionKind.Ssh
                : method.StartsWith("RDP", StringComparison.Ordinal) ? ConnectionKind.Rdp
                : null;
            var host = (e.Text("ip") ?? "").Trim();
            if (kind is null || host.Length == 0)
            {
                unsupported++; // VNC, Telnet, SFTP, FTP, generic commands, … — not in Shellboat yet
                continue;
            }

            var name = e.Text("name") is { Length: > 0 } n ? n : host;
            var user = e.Text("user") ?? "";
            var options = e.Text("options") ?? "";
            var authType = e.Text("auth type") ?? "userpass";
            var keyPath = e.Text("public key") ?? ""; // Ásbrú's name for the private key file
            var useKey = kind == ConnectionKind.Ssh && authType == "publickey" && keyPath.Length > 0;

            if (!string.IsNullOrEmpty(e.Text("jump ip")))
            {
                jumps++;
            }

            var domain = "";
            if (kind == ConnectionKind.Rdp)
            {
                if (CertIgnore().IsMatch(options))
                {
                    certIgnore++;
                }

                if (DomainOption().Match(options) is { Success: true } d)
                {
                    domain = d.Groups[1].Value.Trim('\'', '"');
                }
                else if (user.Contains('\\'))
                {
                    domain = user[..user.IndexOf('\\')];
                    user = user[(user.IndexOf('\\') + 1)..];
                }
            }

            var (secretField, purpose) = useKey ? ("passphrase", SecretPurpose.KeyPassphrase) : ("pass", SecretPurpose.Password);
            if (authType != "manual" && e.Text(secretField) is { Length: > 0 } secret)
            {
                if (LooksEncrypted(secret))
                {
                    encrypted++;
                }
                else
                {
                    secrets.Add(new ImportedSecret(id, purpose, secret));
                }
            }

            var description = e.Text("description") ?? "";
            connections.Add(new ConnectionEntry
            {
                Id = id,
                Name = name,
                FolderId = Parent(e),
                Kind = kind.Value,
                Host = host,
                Port = int.TryParse(e.Text("port"), out var port) ? port : 0,
                UserName = user,
                // Ásbrú pre-fills "Connection with '<name at creation>'"; that's noise, not a note.
                Notes = DefaultDescription().IsMatch(description) ? "" : description,
                Ssh = kind == ConnectionKind.Ssh
                    ? new SshSettings(useKey ? SshAuthMethod.PrivateKey : SshAuthMethod.Password, useKey ? keyPath : null)
                    : null,
                Rdp = kind == ConnectionKind.Rdp ? new RdpSettings(Domain: domain) : null,
            });
        }

        return new AsbruImportResult(new ConnectionTreeDocument(folders, connections), secrets, unsupported, encrypted, jumps, certIgnore);
    }

    /// <summary>Ásbrú's config encrypts with Crypt::CBC and stores the hex of "Salted__…".</summary>
    internal static bool LooksEncrypted(string value)
        => value.Length >= 32 && value.StartsWith("53616c7465645f5f", StringComparison.OrdinalIgnoreCase) && value.All(Uri.IsHexDigit);

    private static Dictionary<Guid, Entry> LoadEntries(string yaml)
    {
        YamlMappingNode root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            root = stream.Documents.FirstOrDefault()?.RootNode as YamlMappingNode
                ?? throw new CredentialImportException(ImportFailureKind.UnexpectedOutput, "This file is empty or is not an Ásbrú export.");
        }
        catch (YamlException ex)
        {
            // Position only: the message of a YAML error can quote file content, which may be a password.
            throw new CredentialImportException(ImportFailureKind.UnexpectedOutput,
                $"This is not a readable Ásbrú export (YAML error at line {ex.Start.Line}, column {ex.Start.Column}).");
        }
        catch (InvalidOperationException)
        {
            // YamlDotNet raises this (not YamlException) for some malformed input.
            throw new CredentialImportException(ImportFailureKind.UnexpectedOutput, "This is not a readable Ásbrú export (the YAML is malformed).");
        }

        // asbru.yml keeps the tree under "environments"; an export has the entries at the top.
        if (root.Children.TryGetValue(new YamlScalarNode("environments"), out var env) && env is YamlMappingNode environments)
        {
            root = environments;
        }

        var entries = new Dictionary<Guid, Entry>();
        foreach (var (key, value) in root.Children)
        {
            if (key is YamlScalarNode { Value: { } k } && Guid.TryParse(k, out var id) && value is YamlMappingNode map)
            {
                entries[id] = new Entry(map);
            }
        }

        if (entries.Count == 0 && !root.Children.Keys.OfType<YamlScalarNode>().Any(k => RootKeys.Contains(k.Value)))
        {
            throw new CredentialImportException(ImportFailureKind.UnexpectedOutput, "No Ásbrú connections found in this file.");
        }

        return entries;
    }

    [GeneratedRegex(@"(?:^|\s)/cert-ignore\b|(?:^|\s)/cert:ignore\b")]
    private static partial Regex CertIgnore();

    [GeneratedRegex(@"^Connection with '.*'$")]
    private static partial Regex DefaultDescription();

    [GeneratedRegex(@"(?:^|\s)/d:(\S+)")]
    private static partial Regex DomainOption();

    private sealed class Entry(YamlMappingNode map)
    {
        public bool IsGroup => Text("_is_group") == "1";

        public Guid? ParentId => Guid.TryParse(Text("parent"), out var p) ? p : null;

        public string? Text(string key)
            => map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode { Value: { } v } && v != "~" ? v : null;
    }
}
