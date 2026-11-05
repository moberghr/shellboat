namespace Shellboat.Core.Settings;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>The terminal's colours: the app theme's, or always light / always dark.</summary>
public enum TerminalColorMode
{
    FollowTheme,
    Light,
    Dark,
}

/// <summary>
/// User preferences (settings.json). No secrets. <see cref="Shortcuts"/> holds only the user's overrides, keyed by
/// action id ("close-tab"); an empty value unbinds the action. Defaults live in the app's shortcut catalog, so new
/// actions get their default without migrating the file.
/// </summary>
public sealed record AppSettings
{
    public const double MinFontSize = 8;
    public const double MaxFontSize = 32;
    public const int MinScrollback = 100;
    public const int MaxScrollback = 100_000;
    public const int MinClipboardSeconds = 5;
    public const int MaxClipboardSeconds = 600;

    public static AppSettings Default { get; } = new();

    public ThemeMode Theme { get; init; } = ThemeMode.System;

    public TerminalColorMode TerminalColors { get; init; } = TerminalColorMode.FollowTheme;

    /// <summary>"#RRGGBB" painted behind the terminal instead of the theme's; null = the theme's own.</summary>
    public string? TerminalBackground { get; init; }

    /// <summary>Null = the built-in monospace fallback list.</summary>
    public string? TerminalFontFamily { get; init; }

    public double TerminalFontSize { get; init; } = 13;

    public bool CopyOnSelect { get; init; } = true;

    public bool RightClickPaste { get; init; } = true;

    /// <summary>Lines of history per terminal; applies to terminals opened after the change.</summary>
    public int ScrollbackLines { get; init; } = 5000;

    public int PasswordClipboardSeconds { get; init; } = 30;

    /// <summary>Ask before closing a tab, window or the app while a session is still connected.</summary>
    public bool ConfirmCloseLiveSessions { get; init; } = true;

    public IReadOnlyDictionary<string, string> Shortcuts { get; init; } = new Dictionary<string, string>();

    /// <summary>Clamps every number into its range and drops a blank font name, so a hand-edited file can't
    /// break the terminal.</summary>
    public AppSettings Normalize() => this with
    {
        TerminalFontFamily = string.IsNullOrWhiteSpace(TerminalFontFamily) ? null : TerminalFontFamily.Trim(),
        TerminalBackground = IsHexColor(TerminalBackground) ? TerminalBackground!.Trim().ToUpperInvariant() : null,
        TerminalFontSize = double.IsFinite(TerminalFontSize) ? Math.Clamp(TerminalFontSize, MinFontSize, MaxFontSize) : 13,
        ScrollbackLines = Math.Clamp(ScrollbackLines, MinScrollback, MaxScrollback),
        PasswordClipboardSeconds = Math.Clamp(PasswordClipboardSeconds, MinClipboardSeconds, MaxClipboardSeconds),
        Shortcuts = Shortcuts ?? new Dictionary<string, string>(),
    };

    /// <summary>True for "#RRGGBB" (six hex digits, leading '#').</summary>
    public static bool IsHexColor(string? value)
    {
        var v = value?.Trim();
        return v is { Length: 7 } && v[0] == '#' && !v.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF");
    }
}

/// <summary>Loads and saves <see cref="AppSettings"/>. A missing file loads as <see cref="AppSettings.Default"/>.</summary>
public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken ct);

    Task SaveAsync(AppSettings settings, CancellationToken ct);
}
