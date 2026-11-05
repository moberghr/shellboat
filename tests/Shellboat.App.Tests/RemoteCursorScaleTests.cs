using Shellboat.App.Rdp;

namespace Shellboat.App.Tests;

public class RemoteCursorScaleTests
{
    [Fact]
    public void Retina_halves_a_200_percent_remote_arrow_and_its_hotspot()
        => Assert.Equal((32, 32, 5, 3), RemoteCursorCache.ScaleShape(64, 64, 10, 6, 2.0));

    [Fact]
    public void Scale_one_keeps_the_bitmap_as_is()
        => Assert.Equal((48, 48, 7, 9), RemoteCursorCache.ScaleShape(48, 48, 7, 9, 1.0));

    [Fact]
    public void Fractional_scaling_rounds_and_keeps_the_hotspot_inside()
        => Assert.Equal((21, 21, 20, 0), RemoteCursorCache.ScaleShape(32, 32, 31, 0, 1.5));

    [Fact]
    public void A_tiny_cursor_never_shrinks_to_nothing()
        => Assert.Equal((1, 1, 0, 0), RemoteCursorCache.ScaleShape(1, 1, 0, 0, 2.0));

    [Fact]
    public void A_hotspot_outside_the_image_is_clamped_even_unscaled()
        => Assert.Equal((32, 32, 31, 31), RemoteCursorCache.ScaleShape(32, 32, 40, 99, 1.0));
}
