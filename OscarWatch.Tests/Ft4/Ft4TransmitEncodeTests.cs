using OscarWatch.Core.Ft4;
using OscarWatch.Ft4;

namespace OscarWatch.Tests.Ft4;

public sealed class Ft4TransmitEncodeTests
{
    private const string Message = "G4ABC MM9SQL -12";

    private static float[] Encode(float freqHz, int rate, float slope = 0f, float gain = 1f)
    {
        Assert.True(Ft8Native.TryEncodeFt4(Message, freqHz, rate, slope, gain, out var pcm, out var error), error);
        return pcm!;
    }

    // At 12 kHz with the 0.5 s lead-in, these uplink slopes put the burst start a whole
    // number of turns into the reference chirp, so the two waveforms line up sample for sample.
    [Theory]
    [InlineData(-8.0, "USB")]
    [InlineData(8.0, "USB")]
    [InlineData(16.0, "LSB")]
    public void Native_slope_matches_the_fft_pre_compensation(double uplinkSlope, string uplinkMode)
    {
        if (!Ft8Native.IsAvailable)
            return;

        var steady = Encode(1500f, 12000);
        var reference = Ft4AudioDoppler.ApplyTxPrecompensation(steady, 12000, uplinkSlope, uplinkMode);
        var audioSlope = (float)Ft4AudioDoppler.TxPrecompAudioSlope(uplinkSlope, uplinkMode);
        var native = Encode(1500f, 12000, audioSlope);

        Assert.Equal(reference.Length, native.Length);
        // Inside the burst, clear of the edges where the FFT reference rings.
        var worst = 0.0;
        for (var i = 8000; i < 64000; i++)
            worst = Math.Max(worst, Math.Abs(native[i] - reference[i]));
        Assert.True(worst < 0.05, $"largest difference {worst:0.0000}");
    }

    [Fact]
    public void Pre_compensated_burst_decodes_at_its_frequency_once_the_drift_is_removed()
    {
        if (!Ft8Native.IsAvailable)
            return;

        const double slope = 20;
        var pcm = Encode(1500f, 12000, (float)slope);
        var steady = Ft4AudioDoppler.RemoveLinearDrift(pcm, 12000, slope);

        var decoded = Ft8Native.DecodeFt4(steady, 12000, centreHz: 1500);
        var hit = Assert.Single(decoded, d => d.text == Message);
        Assert.InRange(hit.freq_hz, 1495f, 1505f);
    }

    [Fact]
    public void Encode_at_48k_decodes_after_decimating_to_12k()
    {
        if (!Ft8Native.IsAvailable)
            return;

        var pcm48 = Encode(1500f, 48000);
        Assert.Equal(48000 * 15 / 2, pcm48.Length);

        var pcm12 = new float[pcm48.Length / 4];
        for (var i = 0; i < pcm12.Length; i++)
            pcm12[i] = (pcm48[4 * i] + pcm48[4 * i + 1] + pcm48[4 * i + 2] + pcm48[4 * i + 3]) / 4;

        var decoded = Ft8Native.DecodeFt4(pcm12, 12000, centreHz: 1500);
        Assert.Contains(decoded, d => d.text == Message && d.ap == 0);
    }

    [Fact]
    public void Gain_scales_the_waveform()
    {
        if (!Ft8Native.IsAvailable)
            return;

        var full = Encode(1500f, 48000);
        var quarter = Encode(1500f, 48000, gain: 0.25f);
        Assert.Equal(1.0, full.Max(Math.Abs), 3);
        Assert.Equal(0.25, quarter.Max(Math.Abs), 3);
    }

    [Fact]
    public void Output_gain_clamps_the_tx_level()
    {
        Assert.Equal(0.01f, Ft4AudioService.OutputGain(0));
        Assert.Equal(0.5f, Ft4AudioService.OutputGain(0.5));
        Assert.Equal(1f, Ft4AudioService.OutputGain(3));
    }
}
