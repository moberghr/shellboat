using Avalonia;

namespace Shellboat.App.Controls;

/// <summary>Pure geometry for dragging tab headers: where a dragged tab goes, and when it tears off.</summary>
public static class TabDragGeometry
{
    /// <summary>How far past the tab strip (or outside the window) the pointer must be before letting go detaches.</summary>
    public const double TearOffDistance = 40;

    /// <summary>
    /// The index the dragged tab should take: the header under the pointer, in strip order. Past either end of a
    /// row it goes to that end; headers may wrap onto several rows.
    /// </summary>
    public static int TargetIndex(IReadOnlyList<Rect> headers, Point pointer)
    {
        if (headers.Count == 0)
        {
            return 0;
        }

        // The row the pointer is on (nearest row vertically), then the header horizontally within it.
        var rowTop = headers.Select(h => h.Top).Distinct()
            .OrderBy(top => Math.Abs(headers.First(h => h.Top == top).Center.Y - pointer.Y))
            .First();
        var row = headers.Select((h, i) => (h, i)).Where(x => x.h.Top == rowTop).OrderBy(x => x.h.Left).ToList();
        foreach (var (h, i) in row)
        {
            if (pointer.X < h.Right)
            {
                return i;
            }
        }

        return row[^1].i;
    }

    /// <summary>True when letting go here should detach the tab: well below/above the strip, or outside the window.</summary>
    public static bool IsTearOff(Rect strip, Size window, Point pointer)
    {
        var outsideWindow = pointer.X < 0 || pointer.Y < 0 || pointer.X > window.Width || pointer.Y > window.Height;
        var farFromStrip = pointer.Y > strip.Bottom + TearOffDistance || pointer.Y < strip.Top - TearOffDistance;
        return outsideWindow || farFromStrip;
    }
}
