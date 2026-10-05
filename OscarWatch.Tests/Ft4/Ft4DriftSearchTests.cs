using OscarWatch.Core.Ft4;
using OscarWatch.Ft4;

namespace OscarWatch.Tests.Ft4;

public sealed class Ft4DriftSearchTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2.5)]
    [InlineData(double.NaN)]
    public void Small_uplink_slope_needs_no_drift_search(double uplinkSlope)
    {
        Assert.Equal(0, Ft4DriftSearch.MaxResidualHzPerSec(uplinkSlope));
        Assert.Equal(0, Ft4DriftSearch.Steps(uplinkSlope));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(-32)]
    public void Range_covers_our_uplink_slope_with_margin(double uplinkSlope)
    {
        var reach = Ft4DriftSearch.MaxResidualHzPerSec(uplinkSlope);
        var steps = Ft4DriftSearch.Steps(uplinkSlope);

        Assert.True(reach >= 40, $"reach {reach}");
        Assert.True(reach / steps <= Ft4DriftSearch.StepHzPerSec + 1e-9, $"spacing {reach / steps}");
    }

    [Fact]
    public void Range_is_capped_near_tca()
    {
        Assert.Equal(Ft4DriftSearch.MaxStepsEachSide, Ft4DriftSearch.Steps(400));
        Assert.Equal(Ft4DriftSearch.StepHzPerSec * Ft4DriftSearch.MaxStepsEachSide, Ft4DriftSearch.MaxResidualHzPerSec(400));
    }

    [Fact]
    public void Station_holding_its_uplink_decodes_in_one_drift_search()
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
        var corrected = Ft4AudioDoppler.RemoveLinearDrift(received, rate, downlinkSlope);

        var plain = Ft8Native.DecodeFt4(corrected, rate, 200f, 2800f);
        Assert.DoesNotContain(plain, d => d.text.Contains("ON8NT", StringComparison.Ordinal));

        var found = Ft8Native.DecodeFt4Drift(
            corrected,
            rate,
            200f,
            2800f,
            deep: false,
            apHints: null,
            apCentreHz: 0,
            maxResidualHzPerSec: (float)Ft4DriftSearch.MaxResidualHzPerSec(32),
            driftSteps: Ft4DriftSearch.Steps(32));
        var hit = Assert.Single(found, d => d.text.Contains("ON8NT", StringComparison.Ordinal));
        Assert.InRange(hit.drift_hz_s, theirUplinkSlope - 4, theirUplinkSlope + 4);
        // The slide is zero at slot start and the decoder reports the middle of the burst.
        var midBurstHz = 1800 + theirUplinkSlope * (hit.time_sec + 105 * 0.048 / 2);
        Assert.InRange(hit.freq_hz, midBurstHz - 15, midBurstHz + 15);
    }
}
