namespace Shellboat.Core.Connections;

/// <summary>
/// A user-chosen icon for a tree row: <see cref="Glyph"/> is a name from the app's built-in set (e.g. "server"),
/// <see cref="Color"/> is "#RRGGBB". Either may be null = the default for that kind. Unknown values (a hand-edited
/// file, an older build) fall back to the default when shown.
/// </summary>
public sealed record NodeIcon(string? Glyph, string? Color);
