using Avalonia;
using Shellboat.App.Controls;

namespace Shellboat.App.Tests;

public class TabDragGeometryTests
{
    // Three 100px headers in one row at y 0..30.
    private static readonly Rect[] OneRow = [new(0, 0, 100, 30), new(100, 0, 100, 30), new(200, 0, 100, 30)];

    [Theory]
    [InlineData(10, 0)]
    [InlineData(150, 1)]
    [InlineData(299, 2)]
    [InlineData(-50, 0)]
    [InlineData(900, 2)]
    public void The_header_under_the_pointer_is_the_target_and_past_the_ends_clamps(double x, int expected)
        => Assert.Equal(expected, TabDragGeometry.TargetIndex(OneRow, new Point(x, 15)));

    [Fact]
    public void Wrapped_rows_pick_the_row_nearest_the_pointer()
    {
        Rect[] twoRows = [new(0, 0, 100, 30), new(100, 0, 100, 30), new(0, 30, 100, 30), new(100, 30, 100, 30)];

        Assert.Equal(3, TabDragGeometry.TargetIndex(twoRows, new Point(150, 45)));
        Assert.Equal(1, TabDragGeometry.TargetIndex(twoRows, new Point(150, 10)));
        Assert.Equal(3, TabDragGeometry.TargetIndex(twoRows, new Point(500, 50)));
    }

    [Fact]
    public void No_headers_means_index_zero()
        => Assert.Equal(0, TabDragGeometry.TargetIndex([], new Point(10, 10)));

    private static readonly Rect Strip = new(300, 40, 600, 30);
    private static readonly Size Window = new(1200, 800);

    [Theory]
    [InlineData(500, 55, false)]   // on the strip
    [InlineData(500, 100, false)]  // just below it: still reordering
    [InlineData(500, 120, true)]   // well below: tear off
    [InlineData(500, -5, true)]    // above the window
    [InlineData(1300, 55, true)]   // right of the window
    [InlineData(-1, 55, true)]     // left of the window
    public void Tear_off_needs_distance_from_the_strip_or_leaving_the_window(double x, double y, bool expected)
        => Assert.Equal(expected, TabDragGeometry.IsTearOff(Strip, Window, new Point(x, y)));
}
