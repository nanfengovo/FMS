using Fms.Application.Map;

namespace Fms.Tests;

public class KecongMapImageTests
{
    [Fact]
    public void Image_is_placed_on_the_map_envelope_using_header_resolution()
    {
        var (originX, topY, resolution) = KecongMapImage.Place(1000, 800, -10, -5, 40, 35, 0.05);

        Assert.Equal(-10, originX);
        Assert.Equal(35, topY);
        Assert.Equal(0.05, resolution);
    }

    [Fact]
    public void Missing_resolution_uses_the_topology_width()
    {
        var (_, _, resolution) = KecongMapImage.Place(100, 50, 0, 0, 20, 10, 0);

        Assert.Equal(0.2, resolution);
    }
}
