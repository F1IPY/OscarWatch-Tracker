using System.Diagnostics;
using OscarWatch.Core.Ft4;
using OscarWatch.Ft4;
using Xunit.Abstractions;

namespace OscarWatch.Tests.Ft4;

/// <summary>
/// Decode rate and timing against synthetic slots, for measuring decoder changes.
/// Slow, so it only runs with OSCARWATCH_FT4_SWEEP=1:
/// <c>dotnet test --filter Category=Ft4Sweep</c>.
/// </summary>
[Trait("Category", "Ft4Sweep")]
public sealed class Ft4DecodeSweepTests(ITestOutputHelper output)
{
    private const int Rate = 12000;
    private const int Seeds = 40;

    private static bool Enabled =>
        Ft8Native.IsAvailable
        && Environment.GetEnvironmentVariable("OSCARWATCH_FT4_SWEEP") == "1";

    [Fact]
    public void Sensitivity_sweep()
    {
        if (!Enabled)
            return;

        const string message = "MM9SQL G4ABC RR73";
        var pcm = Ft8Native.EncodeFt4(message, 1500f)!;
        var falses = 0;
        foreach (var snr in new[] { -14, -16, -18, -20 })
        {
            var hits = 0;
            var sw = Stopwatch.StartNew();
            for (var seed = 1; seed <= Seeds; seed++)
            {
                var noisy = AddWhiteNoise(pcm, snr, seed);
                foreach (var d in Ft8Native.DecodeFt4(noisy, Rate, 200f, 2800f))
                {
                    if (d.text == message)
                        hits++;
                    else
                        falses++;
                }
            }

            output.WriteLine($"{snr,4} dB: {hits,2}/{Seeds} decoded, {sw.Elapsed.TotalMilliseconds / Seeds:0.0} ms per slot");
        }

        output.WriteLine($"false decodes: {falses}");
        Assert.Equal(0, falses);
    }

    [Fact]
    public void Drift_sweep()
    {
        if (!Enabled)
            return;

        const string message = "W9XYZ K1ABC -11";
        var pcm = Ft8Native.EncodeFt4(message, 1500f)!;
        foreach (var slope in new[] { 0.0, 2, 4, 6, 8, 12, 16 })
        {
            // RemoveLinearDrift applies the opposite slope, so negate to add a slide.
            var sliding = Ft4AudioDoppler.RemoveLinearDrift(pcm, Rate, -slope);
            var hits = 0;
            var sw = Stopwatch.StartNew();
            for (var seed = 1; seed <= Seeds / 4; seed++)
            {
                var noisy = AddWhiteNoise(sliding, -12, seed);
                var decoded = Ft8Native.DecodeFt4Drift(
                    noisy, Rate, 200f, 2800f, deep: false, apHints: null, apCentreHz: 0,
                    maxResidualHzPerSec: (float)Ft4DriftSearch.MaxResidualHzPerSec(slope),
                    driftSteps: Ft4DriftSearch.Steps(slope));
                if (decoded.Any(d => d.text == message))
                    hits++;
            }

            output.WriteLine($"{slope,4:0} Hz/s: {hits,2}/{Seeds / 4}, {sw.Elapsed.TotalMilliseconds / (Seeds / 4):0.0} ms per slot");
        }
    }

    private static float[] AddWhiteNoise(float[] pcm, int snrDb, int seed)
    {
        double power = 0;
        var active = 0;
        foreach (var sample in pcm)
        {
            if (Math.Abs(sample) <= 1e-4f)
                continue;
            power += sample * (double)sample;
            active++;
        }

        power /= active;
        var sigma = Math.Sqrt(power * Rate / (Math.Pow(10.0, snrDb / 10.0) * 5000.0));
        var rng = new Random(seed);
        var mixed = new float[pcm.Length];
        for (var i = 0; i < pcm.Length; i++)
        {
            var u1 = 1.0 - rng.NextDouble();
            var u2 = 1.0 - rng.NextDouble();
            mixed[i] = pcm[i] + (float)(sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        return mixed;
    }
}
