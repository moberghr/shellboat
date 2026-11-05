using Shellboat.Core.Connections;
using Shellboat.Core.Import;

namespace Shellboat.App.Connections;

/// <summary>Turns a KeePass entry into a connection guess. Pure; the user reviews every row before import.</summary>
public static class KeePassImportMapper
{
    /// <summary>
    /// Kind and host from the entry URL when it has a scheme (<c>ssh://</c>, <c>rdp://</c>), else from the
    /// title. Port from the URL when given. Unknown schemes default to SSH.
    /// </summary>
    public static ConnectionEntry ToConnection(ImportedEntry entry, Guid? folderId)
    {
        var kind = ConnectionKind.Ssh;
        var host = "";
        var port = 0;
        var user = entry.UserName;

        if (Uri.TryCreate(entry.Url.Trim(), UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            kind = uri.Scheme.ToLowerInvariant() switch
            {
                "rdp" or "ms-rd" => ConnectionKind.Rdp,
                _ => ConnectionKind.Ssh,
            };
            host = uri.Host;
            port = uri.IsDefaultPort || uri.Port < 0 ? 0 : uri.Port;
            if (string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(uri.UserInfo))
            {
                user = Uri.UnescapeDataString(uri.UserInfo.Split(':')[0]);
            }
        }
        else if (!string.IsNullOrWhiteSpace(entry.Url))
        {
            host = entry.Url.Trim();
        }
        else
        {
            host = entry.Title.Trim();
        }

        // A bare "host:port" with no scheme.
        if (port == 0 && host.Count(c => c == ':') == 1 && int.TryParse(host[(host.IndexOf(':') + 1)..], out var p))
        {
            port = p;
            host = host[..host.IndexOf(':')];
        }

        if (port == 3389)
        {
            kind = ConnectionKind.Rdp;
        }

        var domain = "";
        if (kind == ConnectionKind.Rdp && user.Contains('\\'))
        {
            domain = user[..user.IndexOf('\\')];
            user = user[(user.IndexOf('\\') + 1)..];
        }

        return new ConnectionEntry
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(entry.Title) ? host : entry.Title.Trim(),
            FolderId = folderId,
            Kind = kind,
            Host = host,
            Port = port,
            UserName = user,
            Notes = entry.Notes,
            Ssh = kind == ConnectionKind.Ssh ? new SshSettings(SshAuthMethod.Password) : null,
            Rdp = kind == ConnectionKind.Rdp ? new RdpSettings(Domain: domain) : null,
        };
    }
}
