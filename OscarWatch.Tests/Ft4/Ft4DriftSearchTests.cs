using OscarWatch.Core.Ft4;
using OscarWatch.Ft4;

namespace OscarWatch.Tests.Ft4;

public sealed class Ft4DriftSearchTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2.5)]
    [InlineData(double.NaN)]
    public void Small_uplink_slope_needs_no_extra_passes(double uplinkSlope)
    {
        Assert.Empty(Ft4DriftSearch.ResidualSlopes(uplinkSlope));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(-32)]
    public void Grid_covers_our_uplink_slope_on_both_sides(double uplinkSlope)
    {
        var slopes = Ft4DriftSearch.ResidualSlopes(uplinkSlope);

        foreach (var target in new[] { 32.0, -32.0, 40.0, -40.0 })
            Assert.Contains(slopes, s => Math.Abs(s - target) <= Ft4DriftSearch.StepHzPerSec / 2);
        Assert.DoesNotContain(0.0, slopes);
    }

    [Fact]
    public void Grid_is_capped_near_tca()
    {
        var slopes = Ft4DriftSearch.ResidualSlopes(400);

        Assert.Equal(Ft4DriftSearch.MaxStepsEachSide * 2, slopes.Count);
    }

    [Fact]
    public void Station_holding_its_uplink_decodes_on_a_grid_pass()
    {
        if (!Ft8Native.IsAvailable)
            return;

        const int rate = 12000;
        const double downlinkSlope = -11;
        const double theirUplinkSlope = 30;
        var pcm = Ft8Native.EncodeFt4("CQ ON8NT JO11", freqHz: 1800f)!;
        var slot = new float[rate * 7];
        Array.Copy(pcm, 0, slot, rate / 2, Math.Min(pcm.Length, slot.Length - rate / 2));
        var received = Ft4AudioDoppler.RemoveLinearDrift(slot, rate, -(downlinkSlope + theirUplinkSlope));

        var plain = Ft8Native.DecodeFt4(Ft4AudioDoppler.RemoveLinearDrift(received, rate, downlinkSlope), rate, 200f, 2800f);
        Assert.DoesNotContain(plain, d => d.text.Contains("ON8NT", StringComparison.Ordinal));

        var found = Ft4DriftSearch.ResidualSlopes(uplinkSlopeHzPerSec: 32).Any(residual =>
        {
            var aligned = Ft4AudioDoppler.RemoveLinearDrift(received, rate, downlinkSlope + residual);
            return Ft8Native.DecodeFt4(aligned, rate, 200f, 2800f)
                .Any(d => d.text.Contains("ON8NT", StringComparison.Ordinal));
        });
        Assert.True(found);
    }
}
