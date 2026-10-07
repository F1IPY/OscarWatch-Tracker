using OscarWatch.Controls;
using OscarWatch.Core.Geo;

namespace OscarWatch.Tests;

public class WorldMapHitTests
{
    [Fact]
    public void PixelToGeo_inverts_GeoToPixel_away_from_the_poles()
    {
        const double width = 800;
        const double height = 400;
        const double centre = 20;
        var (x, y) = EquirectangularProjection.GeoToPixel(51.5, -0.1, width, height, centre);

        var (lat, lon) = WorldMapControl.PixelToGeo(x, y, width, height, centre);

        Assert.Equal(51.5, lat, 3);
        Assert.Equal(-0.1, lon, 3);
    }

    [Fact]
    public void Label_bounds_cover_the_name_above_the_subpoint()
    {
        var bounds = WorldMapControl.SatelliteLabelBounds(100, 200, textWidth: 40, textHeight: 12);

        Assert.True(bounds.Contains(new Avalonia.Point(100, 186)));
        Assert.False(bounds.Contains(new Avalonia.Point(100, 200)));
    }

    [Fact]
    public void Overlapping_footprints_select_the_smaller_satellite()
    {
        var hit = WorldMapControl.ChooseSmallestFootprintHit(
        [
            ("geo", RadiusDeg: 70, AngularDistanceDeg: 10),
            ("leo", RadiusDeg: 12, AngularDistanceDeg: 8),
            ("miss", RadiusDeg: 5, AngularDistanceDeg: 9)
        ]);

        Assert.Equal("leo", hit);
    }

    [Fact]
    public void Footprint_miss_selects_nothing()
    {
        var hit = WorldMapControl.ChooseSmallestFootprintHit(
        [
            ("ao-07", RadiusDeg: 15, AngularDistanceDeg: 20)
        ]);

        Assert.Null(hit);
    }
}
