namespace Shellboat.App.Services;

/// <summary>
/// Where downloads land and how remote names become local ones. A remote name is server-controlled input: it must
/// never carry a path out of the target folder ("../x", "/etc/x", "C:\x") or overwrite an existing file.
/// </summary>
public static class LocalDownloads
{
    private static readonly string[] WindowsReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>The user's Downloads folder, or their home folder when there is none.</summary>
    public static string DefaultFolder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloads = Path.Combine(home, "Downloads");
        return Directory.Exists(downloads) ? downloads : home;
    }

    /// <summary>A single, harmless file-name component for <paramref name="remoteName"/>, valid on every OS we run on.</summary>
    public static string SafeName(string remoteName)
    {
        var chars = remoteName.Select(c => c < 32 || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c).ToArray();
        var name = new string(chars).Trim().TrimEnd('.');
        if (name.Length == 0)
        {
            name = "_" + name;
        }

        var stem = name.Split('.')[0];
        if (WindowsReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
        {
            name = "_" + name;
        }

        return name;
    }

    /// <summary>A path in <paramref name="folder"/> for <paramref name="remoteName"/> that is not taken yet:
    /// "report.pdf", then "report (1).pdf", "report (2).pdf", ….</summary>
    public static string UniquePath(string folder, string remoteName)
    {
        var name = SafeName(remoteName);
        var candidate = Path.Combine(folder, name);
        var ext = Path.GetExtension(name);
        var stem = ext.Length > 0 && ext.Length < name.Length ? name[..^ext.Length] : name;
        if (stem == name)
        {
            ext = "";
        }

        for (var i = 1; File.Exists(candidate) || Directory.Exists(candidate); i++)
        {
            candidate = Path.Combine(folder, $"{stem} ({i}){ext}");
        }

        return candidate;
    }
}
