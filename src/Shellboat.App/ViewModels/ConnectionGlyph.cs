using Shellboat.Core.Connections;

namespace Shellboat.App.ViewModels;

/// <summary>Connection kind → the glyph and tint resource keys in Themes/Tokens.axaml (tree rows and tabs share it).</summary>
public static class ConnectionGlyph
{
    public const string FolderIcon = "Icon.Folder";

    public const string FolderBrush = "Text.Muted";

    public static string IconKey(ConnectionKind kind) => kind == ConnectionKind.Rdp ? "Icon.Rdp" : "Icon.Ssh";

    public static string BrushKey(ConnectionKind kind) => kind == ConnectionKind.Rdp ? "Kind.Rdp" : "Kind.Ssh";
}
