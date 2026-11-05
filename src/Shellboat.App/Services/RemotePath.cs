namespace Shellboat.App.Services;

/// <summary>SFTP paths are always '/'-separated, whatever the local OS.</summary>
public static class RemotePath
{
    public static string Combine(string directory, string name)
        => directory.EndsWith('/') ? directory + name : $"{directory}/{name}";

    /// <summary>The parent directory, or null at the root (or for a relative path with no parent).</summary>
    public static string? Parent(string path)
    {
        var trimmed = path.Length > 1 ? path.TrimEnd('/') : path;
        if (trimmed == "/" || trimmed.Length == 0)
        {
            return null;
        }

        var slash = trimmed.LastIndexOf('/');
        if (slash < 0)
        {
            return null;
        }

        return slash == 0 ? "/" : trimmed[..slash];
    }
}
