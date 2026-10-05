using OscarWatch.Core.Ft4;

namespace OscarWatch.Tests.Ft4;

public sealed class Ft4AudioDopplerTests
{
    [Fact]
    public void Tx_precomp_sign_flips_for_lsb_uplink()
    {
        Assert.Equal(-1.0, Ft4AudioDoppler.TxPrecompSign("USB"));
        Assert.Equal(-1.0, Ft4AudioDoppler.TxPrecompSign("DATA-USB"));
        Assert.Equal(1.0, Ft4AudioDoppler.TxPrecompSign("LSB"));
        Assert.Equal(1.0, Ft4AudioDoppler.TxPrecompSign("DATA-LSB"));
    }

    [Fact]
    public void RemoveLinearDrift_noop_for_zero_slope()
    {
        var pcm = new float[256];
        for (var i = 0; i < pcm.Length; i++)
            pcm[i] = (float)Math.Sin(2 * Math.PI * 1500 * i / 12000.0);

        var outPcm = Ft4AudioDoppler.RemoveLinearDrift(pcm, 12000, 0);
        Assert.Same(pcm, outPcm);
    }

    [Fact]
    public void RemoveLinearDrift_preserves_length_and_energy_for_small_slope()
    {
        const int n = 2048;
        var pcm = new float[n];
        for (var i = 0; i < n; i++)
            pcm[i] = (float)Math.Sin(2 * Math.PI * 1500 * i / 12000.0);

        var outPcm = Ft4AudioDoppler.RemoveLinearDrift(pcm, 12000, slopeHzPerSec: 2.0);
        Assert.Equal(n, outPcm.Length);

        double eIn = 0, eOut = 0;
        for (var i = 0; i < n; i++)
        {
            eIn += pcm[i] * pcm[i];
            eOut += outPcm[i] * outPcm[i];
        }

        Assert.InRange(eOut / eIn, 0.5, 2.0);
    }

    [Theory]
    [InlineData(4.0)]
    [InlineData(-12.0)]
    [InlineData(30.0)]
    public void RemoveLinearDrift_turns_a_full_slot_chirp_back_into_a_steady_tone(double slope)
    {
        const int rate = 12000;
        const int n = 90000;
        const double f0 = 1500;
        var chirp = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)rate;
            chirp[i] = (float)Math.Cos(2 * Math.PI * f0 * t + Math.PI * slope * t * t);
        }

        var steady = Ft4AudioDoppler.RemoveLinearDrift(chirp, rate, slope);

        // Away from the ends, where the one-sided spectrum is exact.
        var worst = 0.0;
        for (var i = 2000; i < n - 2000; i++)
        {
            var expected = Math.Cos(2 * Math.PI * f0 * i / rate);
            worst = Math.Max(worst, Math.Abs(steady[i] - expected));
        }

        Assert.True(worst < 0.02, $"largest error {worst:0.0000}");
    }

    [Fact]
    public void Shared_analytic_signal_matches_a_fresh_build_for_every_slope()
    {
        const int rate = 12000;
        var pcm = new float[30000];
        var rng = new Random(3);
        for (var i = 0; i < pcm.Length; i++)
            pcm[i] = (float)(rng.NextDouble() - 0.5);

        var shared = Ft4AudioDoppler.BuildAnalytic(pcm, rate);
        foreach (var slope in new[] { -16.0, -8, 2, 8, 16 })
            Assert.Equal(Ft4AudioDoppler.RemoveLinearDrift(pcm, rate, slope), shared.RemoveLinearDrift(slope));
    }

    [Fact]
    public void Doppler_shift_slope_from_range_rate_change_is_nonzero()
    {
        var mode = new Core.Models.SatelliteTransponderMode
        {
            Type = "FT4",
            DownlinkKHz = 435_500,
            UplinkKHz = 145_900,
            DownlinkMode = "USB",
            UplinkMode = "LSB",
            Doppler = "REV"
        };

        var s0 = Ft4DopplerShift.ComputeShiftsHz(mode, rangeRateKmPerSec: -2.0);
        var s1 = Ft4DopplerShift.ComputeShiftsHz(mode, rangeRateKmPerSec: -1.0);
        var slope = Ft4DopplerShift.SlopeHzPerSec(s0.DownlinkHz, s1.DownlinkHz, 1.0);
        Assert.NotEqual(0, slope);
        Assert.True(Math.Abs(s0.DownlinkHz) > Math.Abs(s1.DownlinkHz));
    }
}
