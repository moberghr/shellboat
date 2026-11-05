using Shellboat.Core.Connections;

namespace Shellboat.App.ViewModels;

/// <summary>Connection kind → the default glyph and tint resource keys in Themes/Tokens.axaml (tree rows and tabs
/// share it). A user-chosen <see cref="NodeIcon"/> overrides these through <see cref="Connections.IconCatalog"/>.</summary>
public static class ConnectionGlyph
{
    public const string FolderIcon = "Icon.Folder";

    public const string FolderBrush = "Text.Muted";

    /// <summary>The catalog names that look like the defaults, so picking them means "no override".</summary>
    public const string FolderGlyphName = "folder";

    public static string IconKey(ConnectionKind kind) => kind == ConnectionKind.Rdp ? "Icon.Rdp" : "Icon.Ssh";

    public static string BrushKey(ConnectionKind kind) => kind == ConnectionKind.Rdp ? "Kind.Rdp" : "Kind.Ssh";

    public static string GlyphName(ConnectionKind kind) => kind == ConnectionKind.Rdp ? "monitor" : "terminal";
}
