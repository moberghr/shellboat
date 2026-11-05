using Shellboat.Core.Settings;
using Shellboat.Terminal;

namespace Shellboat.App.Terminal;

/// <summary>
/// The terminal's colours as packed 0xRRGGBB (selection is 0xAARRGGBB). The 16 ANSI colours differ per theme
/// (dark-tuned Tango is unreadable on white); 16–255 are the standard xterm cube from <see cref="TerminalPalette"/>.
/// </summary>
public sealed record TerminalTheme(uint Background, uint Foreground, uint Cursor, uint Selection, IReadOnlyList<uint> Ansi)
{
    public static TerminalTheme Dark { get; } = new(0x0F1319, 0xD8DEE6, 0x35D0BE, 0x5535D0BE,
    [
        0x2E3436, 0xCC0000, 0x4E9A06, 0xC4A000, 0x3465A4, 0x75507B, 0x06989A, 0xD3D7CF,
        0x555753, 0xEF2929, 0x8AE234, 0xFCE94F, 0x729FCF, 0xAD7FA8, 0x34E2E2, 0xEEEEEC,
    ]);

    /// <summary>One Half Light–style ANSI set: every colour readable on the near-white background.</summary>
    public static TerminalTheme Light { get; } = new(0xFAFBFC, 0x2B313A, 0x0E8F81, 0x450E8F81,
    [
        0x383A42, 0xD03A40, 0x3E8E3C, 0xA87400, 0x1F6FB3, 0x9A2A9C, 0x0A8597, 0xC9CDD3,
        0x5C6370, 0xE0565E, 0x4FA34C, 0xC18A10, 0x3B8EDB, 0xB549B8, 0x1BA3B5, 0x8C939D,
    ]);

    public static TerminalTheme Resolve(TerminalColorMode mode, bool appIsDark) => mode switch
    {
        TerminalColorMode.Light => Light,
        TerminalColorMode.Dark => Dark,
        _ => appIsDark ? Dark : Light,
    };

    /// <summary>
    /// As <see cref="Resolve(TerminalColorMode, bool)"/>, but a custom background ("#RRGGBB") replaces the theme's and
    /// picks the text palette itself — dark palette on a dark background, light on a light one — so text stays
    /// readable whatever the colour mode says. An unparsable value is ignored.
    /// </summary>
    public static TerminalTheme Resolve(TerminalColorMode mode, bool appIsDark, string? customBackground)
    {
        if (TryParseRgb(customBackground) is not { } bg)
        {
            return Resolve(mode, appIsDark);
        }

        var palette = Luma(bg) < 128 ? Dark : Light;
        return palette with { Background = bg };
    }

    /// <summary>"#RRGGBB" → 0xRRGGBB, or null.</summary>
    public static uint? TryParseRgb(string? hex)
    {
        if (!AppSettings.IsHexColor(hex))
        {
            return null;
        }

        return uint.Parse(hex!.Trim().AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static double Luma(uint rgb) => 0.299 * (rgb >> 16 & 0xFF) + 0.587 * (rgb >> 8 & 0xFF) + 0.114 * (rgb & 0xFF);

    /// <summary>An indexed colour (0–255) in this theme.</summary>
    public uint Rgb(int index) => index is >= 0 and < 16 ? Ansi[index] : TerminalPalette.Rgb(index);
}
