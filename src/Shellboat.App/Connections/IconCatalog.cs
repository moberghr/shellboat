using Shellboat.Core.Connections;

namespace Shellboat.App.Connections;

/// <summary>A pickable glyph: <see cref="Name"/> is what's stored, <see cref="ResourceKey"/> its geometry in Tokens.axaml.</summary>
public sealed record GlyphChoice(string Name, string Label)
{
    public string ResourceKey => $"Glyph.{Name}";
}

/// <summary>A pickable tint; <see cref="Hex"/> is what's stored.</summary>
public sealed record ColorChoice(string Hex, string Label);

/// <summary>
/// The built-in icon set and colour palette for tree rows, and how a stored <see cref="NodeIcon"/> resolves to
/// what is drawn. Pure: an unknown glyph or a malformed colour falls back to the default, never throws.
/// </summary>
public static class IconCatalog
{
    public static IReadOnlyList<GlyphChoice> Glyphs { get; } =
    [
        new("terminal", "Terminal"),
        new("monitor", "Desktop"),
        new("folder", "Folder"),
        new("server", "Server"),
        new("database", "Database"),
        new("cloud", "Cloud"),
        new("globe", "Web"),
        new("router", "Network"),
        new("shield", "Firewall"),
        new("lock", "Secure"),
        new("key", "Key"),
        new("star", "Favourite"),
        new("house", "Home"),
        new("building", "Office"),
        new("gear", "Service"),
        new("box", "Container"),
        new("bolt", "Production"),
        new("flask", "Test"),
        new("chart", "Monitoring"),
        new("laptop", "Workstation"),
        new("tag", "Tag"),
        new("heart", "Heart"),
    ];

    /// <summary>The theme's accent family (Themes/Tokens.axaml) plus a few extra hues that stay legible on graphite.</summary>
    public static IReadOnlyList<ColorChoice> Colors { get; } =
    [
        new("#35D0BE", "Teal"),
        new("#6FA6E2", "Blue"),
        new("#978BE4", "Violet"),
        new("#E07CC6", "Pink"),
        new("#D2555A", "Red"),
        new("#E8925A", "Orange"),
        new("#E3B457", "Amber"),
        new("#98BB6C", "Green"),
        new("#D8DEE6", "White"),
        new("#8B95A1", "Grey"),
    ];

    public static bool IsKnownGlyph(string? name) => name is not null && Glyphs.Any(g => g.Name == name);

    /// <summary>"#RRGGBB" only — the format the picker writes.</summary>
    public static bool IsValidColor(string? hex)
        => hex is { Length: 7 } && hex[0] == '#' && hex.Skip(1).All(Uri.IsHexDigit);

    /// <summary>Resource key of the glyph to draw: the chosen one when valid, else <paramref name="defaultKey"/>.</summary>
    public static string GlyphKey(NodeIcon? icon, string defaultKey)
        => IsKnownGlyph(icon?.Glyph) ? $"Glyph.{icon!.Glyph}" : defaultKey;

    /// <summary>"#RRGGBB" when a valid colour was chosen, else <paramref name="defaultBrushKey"/> (a theme brush key).</summary>
    public static string BrushKey(NodeIcon? icon, string defaultBrushKey)
        => IsValidColor(icon?.Color) ? icon!.Color! : defaultBrushKey;

    /// <summary>Normalises a picker result: defaults become null, and an all-default icon is null altogether.</summary>
    public static NodeIcon? Normalize(string? glyph, string? color, string defaultGlyph)
    {
        var g = IsKnownGlyph(glyph) && glyph != defaultGlyph ? glyph : null;
        var c = IsValidColor(color) ? color!.ToUpperInvariant() : null;
        return g is null && c is null ? null : new NodeIcon(g, c);
    }
}
