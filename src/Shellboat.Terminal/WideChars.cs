namespace Shellboat.Terminal;

/// <summary>
/// East-Asian wide / fullwidth / emoji ranges (from Markus Kuhn's wcwidth plus the common emoji blocks).
/// Used by the vendored engine instead of its own width table, whose binary search reads out of bounds.
/// </summary>
public static class WideChars
{
    public static bool IsWide(int cp)
        => cp >= 0x1100 &&
           (cp <= 0x115F ||
            cp == 0x2329 || cp == 0x232A ||
            (cp >= 0x2E80 && cp <= 0xA4CF && cp != 0x303F) ||
            (cp >= 0xAC00 && cp <= 0xD7A3) ||
            (cp >= 0xF900 && cp <= 0xFAFF) ||
            (cp >= 0xFE10 && cp <= 0xFE19) ||
            (cp >= 0xFE30 && cp <= 0xFE6F) ||
            (cp >= 0xFF00 && cp <= 0xFF60) ||
            (cp >= 0xFFE0 && cp <= 0xFFE6) ||
            (cp >= 0x1F300 && cp <= 0x1F64F) ||
            (cp >= 0x1F900 && cp <= 0x1F9FF) ||
            (cp >= 0x20000 && cp <= 0x2FFFD) ||
            (cp >= 0x30000 && cp <= 0x3FFFD));
}
