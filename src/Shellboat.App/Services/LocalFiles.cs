namespace Shellboat.App.Services;

/// <summary>One entry of a local folder listing (the left pane of the SFTP tab).</summary>
public sealed record LocalFileEntry(string Name, string FullPath, bool IsDirectory, bool IsSymbolicLink, long Size, DateTimeOffset Modified);

/// <summary>The local file system as the SFTP tab's left pane sees it: list, rename and create folders. Never deletes
/// or overwrites.</summary>
public static class LocalFiles
{
    public static string HomeFolder() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Folders first, then files, each by name. Entries we can't stat (permissions, races) are left out.</summary>
    public static IReadOnlyList<LocalFileEntry> List(string folder)
    {
        var entries = new List<LocalFileEntry>();
        foreach (var info in new DirectoryInfo(folder).EnumerateFileSystemInfos())
        {
            try
            {
                var isLink = info.LinkTarget is not null;
                var isDirectory = info is DirectoryInfo;
                var size = info is FileInfo f ? (isLink ? new FileInfo(info.FullName).Length : f.Length) : 0;
                entries.Add(new LocalFileEntry(info.Name, info.FullName, isDirectory, isLink, size, info.LastWriteTimeUtc));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A dangling link or an entry that vanished: skip it rather than fail the whole listing.
            }
        }

        return [.. entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The parent folder, or null at a root ("/", "C:\").</summary>
    public static string? Parent(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        return Path.GetDirectoryName(trimmed) is { Length: > 0 } parent ? parent : null;
    }

    /// <summary>Null when <paramref name="name"/> is a valid single local file name; otherwise why not.</summary>
    public static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "The name can't be empty.";
        }

        if (name is "." or "..")
        {
            return "\".\" and \"..\" aren't valid names.";
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
        {
            return "The name contains a character that isn't allowed in a file name.";
        }

        return null;
    }

    /// <summary>Renames inside the same folder; fails when the target exists.</summary>
    public static string Rename(string path, string newName)
    {
        var target = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path))!, newName);
        if (File.Exists(target) || Directory.Exists(target))
        {
            throw new IOException($"\"{newName}\" already exists.");
        }

        if (Directory.Exists(path))
        {
            Directory.Move(path, target);
        }
        else
        {
            File.Move(path, target, overwrite: false);
        }

        return target;
    }

    /// <summary>Creates <paramref name="name"/> in <paramref name="folder"/>; fails when it exists.</summary>
    public static string CreateFolder(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new IOException($"\"{name}\" already exists.");
        }

        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Files and bytes an upload of <paramref name="paths"/> will copy (symlinked folders are not followed,
    /// matching <see cref="SftpUploader"/>). Unreadable parts are skipped — the upload itself reports them.</summary>
    public static (int Files, long Bytes) Totals(IEnumerable<string> paths)
    {
        var files = 0;
        long bytes = 0;
        foreach (var path in paths)
        {
            Walk(Path.TrimEndingDirectorySeparator(path));
        }

        return (files, bytes);

        void Walk(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    if (new DirectoryInfo(path).LinkTarget is not null)
                    {
                        return;
                    }

                    foreach (var child in Directory.EnumerateFileSystemEntries(path))
                    {
                        Walk(child);
                    }

                    return;
                }

                bytes += new FileInfo(path).Length;
                files++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Counted as nothing; the copy reports the real error.
            }
        }
    }
}
