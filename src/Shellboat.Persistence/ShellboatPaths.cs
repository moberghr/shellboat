namespace Shellboat.Persistence;

/// <summary>
/// Per-user locations for Shellboat's state, following each platform's convention (same rules as Bearing):
/// <c>~/Library/Application Support</c> on macOS, <c>%APPDATA%</c> on Windows, XDG on Linux. An explicit
/// <c>XDG_CONFIG_HOME</c> wins everywhere, so tests and portable installs can redirect state.
/// </summary>
public static class ShellboatPaths
{
    /// <summary>
    /// <c>shellboat</c>, or <c>shellboat-&lt;profile&gt;</c> when <c>SHELLBOAT_PROFILE</c> is set — an isolated
    /// config + keychain namespace so a build from source never touches the installed app's data.
    /// </summary>
    public static string AppDirName
    {
        get
        {
            var profile = Environment.GetEnvironmentVariable("SHELLBOAT_PROFILE");
            if (string.IsNullOrWhiteSpace(profile))
            {
                return "shellboat";
            }

            var clean = new string(profile.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
            return $"shellboat-{clean}";
        }
    }

    public static string ConfigDir => EnsureDir(Path.Combine(ConfigRoot, AppDirName));

    public static string ConnectionsFile => Path.Combine(ConfigDir, "connections.json");

    public static string SettingsFile => Path.Combine(ConfigDir, "settings.json");

    public static string TrustFile => Path.Combine(ConfigDir, "known_hosts.json");

    internal enum PathPlatform { Linux, Windows, MacOS }

    private static string ConfigRoot => ResolveConfigRoot(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
        OperatingSystem.IsWindows() ? PathPlatform.Windows : OperatingSystem.IsMacOS() ? PathPlatform.MacOS : PathPlatform.Linux,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    internal static string ResolveConfigRoot(string? xdgOverride, PathPlatform platform, string home, string roamingAppData)
    {
        if (!string.IsNullOrWhiteSpace(xdgOverride))
        {
            return xdgOverride;
        }

        return platform switch
        {
            PathPlatform.MacOS => Path.Combine(home, "Library", "Application Support"),
            PathPlatform.Windows => roamingAppData,
            _ => Path.Combine(home, ".config"),
        };
    }

    private static string EnsureDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
