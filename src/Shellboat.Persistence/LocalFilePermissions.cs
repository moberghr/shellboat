namespace Shellboat.Persistence;

/// <summary>Restricts a local file to its owner (0600). Best-effort; a no-op on Windows where the per-user
/// profile directory is already ACL'd to the user.</summary>
public static class LocalFilePermissions
{
    public static void Harden(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception)
        {
            // A filesystem that cannot express the mode must not stop the app.
        }
    }
}
