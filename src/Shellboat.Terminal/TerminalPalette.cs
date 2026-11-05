namespace Shellboat.Terminal;

/// <summary>The xterm 256-colour palette as packed 0xRRGGBB. 0–15 are a Tango-like ANSI set.</summary>
public static class TerminalPalette
{
    private static readonly uint[] Colors = Build();

    public static uint Rgb(int index) => index is >= 0 and < 256 ? Colors[index] : 0;

    private static uint[] Build()
    {
        var colors = new uint[256];
        uint[] ansi =
        [
            0x2E3436, 0xCC0000, 0x4E9A06, 0xC4A000, 0x3465A4, 0x75507B, 0x06989A, 0xD3D7CF,
            0x555753, 0xEF2929, 0x8AE234, 0xFCE94F, 0x729FCF, 0xAD7FA8, 0x34E2E2, 0xEEEEEC,
        ];
        ansi.CopyTo(colors, 0);

        int[] steps = [0x00, 0x5F, 0x87, 0xAF, 0xD7, 0xFF];
        for (var i = 0; i < 216; i++)
        {
            var r = (uint)steps[i / 36 % 6];
            var g = (uint)steps[i / 6 % 6];
            var b = (uint)steps[i % 6];
            colors[16 + i] = r << 16 | g << 8 | b;
        }

        for (var i = 0; i < 24; i++)
        {
            var c = (uint)(8 + i * 10);
            colors[232 + i] = c << 16 | c << 8 | c;
        }

        return colors;
    }
}
