namespace OscarWatch.Core.Ft4;

/// <summary>
/// Audio-domain Doppler helpers matching OrbitDeck iOS FT4
/// (<c>HilbertDeDoppler</c> / TX pre-comp in <c>FT4Engine</c>).
/// </summary>
public static class Ft4AudioDoppler
{
    /// <summary>
    /// Remove (or apply, when <paramref name="slopeHzPerSec"/> is negated) a linear
    /// frequency drift from a real mono buffer. Drift at t=0 is treated as zero so
    /// tones keep their slot-start audio positions. Uses an FFT Hilbert analytic
    /// signal and SSB-mixes with <c>exp(-j π slope t²)</c>.
    /// </summary>
    public static float[] RemoveLinearDrift(float[] samples, double sampleRate, double slopeHzPerSec)
    {
        if (!IsUsableSlope(samples, sampleRate, slopeHzPerSec))
            return samples;

        return BuildAnalytic(samples, sampleRate).RemoveLinearDrift(slopeHzPerSec);
    }

    private static bool IsUsableSlope(float[] samples, double sampleRate, double slopeHzPerSec) =>
        samples.Length > 64
        && sampleRate >= 1000
        && double.IsFinite(slopeHzPerSec)
        && Math.Abs(slopeHzPerSec) >= 1e-9;

    /// <summary>
    /// Analytic (one-sided) form of a slot, built once so several drift slopes can be
    /// removed from the same capture without repeating the FFT pair.
    /// </summary>
    public sealed class AnalyticSignal
    {
        private const int ReseedInterval = 1024;
        private readonly float[] _source;
        private readonly double[] _re;
        private readonly double[] _im;

        internal AnalyticSignal(float[] source, double sampleRate, double[] re, double[] im)
        {
            _source = source;
            SampleRate = sampleRate;
            _re = re;
            _im = im;
        }

        public double SampleRate { get; }

        /// <summary>Same result as <see cref="Ft4AudioDoppler.RemoveLinearDrift"/> on the source samples.</summary>
        public float[] RemoveLinearDrift(double slopeHzPerSec)
        {
            if (!IsUsableSlope(_source, SampleRate, slopeHzPerSec))
                return _source;

            // Mix with exp(-jθn), θn = α n². The step exp(jα(2n+1)) itself turns by
            // exp(j2α) each sample, so two complex products replace Cos/Sin per sample.
            // Reseeding from the exact angle keeps rounding from building up.
            var n = _source.Length;
            var alpha = Math.PI * slopeHzPerSec / (SampleRate * SampleRate);
            var turnRe = Math.Cos(2 * alpha);
            var turnIm = Math.Sin(2 * alpha);
            double zRe = 1, zIm = 0, stepRe = 1, stepIm = 0;
            var output = new float[n];
            for (var i = 0; i < n; i++)
            {
                if (i % ReseedInterval == 0)
                {
                    var theta = alpha * i * (double)i;
                    zRe = Math.Cos(theta);
                    zIm = Math.Sin(theta);
                    var stepAngle = alpha * (2.0 * i + 1);
                    stepRe = Math.Cos(stepAngle);
                    stepIm = Math.Sin(stepAngle);
                }

                // Re{ a · e^{-jθ} } = ar·cosθ + ai·sinθ
                output[i] = (float)(_re[i] * zRe + _im[i] * zIm);

                var nextZRe = zRe * stepRe - zIm * stepIm;
                zIm = zRe * stepIm + zIm * stepRe;
                zRe = nextZRe;
                var nextStepRe = stepRe * turnRe - stepIm * turnIm;
                stepIm = stepRe * turnIm + stepIm * turnRe;
                stepRe = nextStepRe;
            }

            return output;
        }
    }

    /// <summary>One-sided spectrum of <paramref name="samples"/>, already scaled by 1/N.</summary>
    public static AnalyticSignal BuildAnalytic(float[] samples, double sampleRate)
    {
        var nIn = samples.Length;
        var nfft = 1;
        while (nfft < nIn)
            nfft <<= 1;

        var re = new double[nfft];
        var im = new double[nfft];
        for (var i = 0; i < nIn; i++)
            re[i] = samples[i];

        FftInPlace(re, im, inverse: false);

        // Analytic signal: double positive freqs, zero negative; leave DC/Nyquist.
        var half = nfft / 2;
        for (var k = 1; k < half; k++)
        {
            re[k] *= 2;
            im[k] *= 2;
        }

        for (var k = half + 1; k < nfft; k++)
        {
            re[k] = 0;
            im[k] = 0;
        }

        FftInPlace(re, im, inverse: true);

        var inv = 1.0 / nfft;
        for (var i = 0; i < nIn; i++)
        {
            re[i] *= inv;
            im[i] *= inv;
        }

        return new AnalyticSignal(samples, sampleRate, re, im);
    }

    /// <summary>
    /// TX audio pre-compensation slope sign for OrbitDeck parity.
    /// USB uplink: RF = dial + audio → cancel with −slope.
    /// LSB / DATA-LSB uplink: RF = dial − audio → flip sign.
    /// </summary>
    public static double TxPrecompSign(string? uplinkMode)
    {
        if (string.IsNullOrWhiteSpace(uplinkMode))
            return -1.0;
        var m = uplinkMode.Trim().ToUpperInvariant();
        if (m is "LSB" or "DATA-LSB" or "DIGL" or "PKTLSB" or "LSB-D")
            return 1.0;
        return -1.0;
    }

    /// <summary>
    /// Apply TX pre-comp to encoded PCM so emitted RF stays fixed while the CAT dial
    /// is held for the slot. <paramref name="uplinkDopplerSlopeHzPerSec"/> is the
    /// ephemeris uplink Doppler-shift slope (Hz/s), same convention as
    /// <see cref="OscarWatch.Core.Radio.DopplerFrequencyCalculator"/> shift.
    /// </summary>
    public static float[] ApplyTxPrecompensation(
        float[] pcm,
        double sampleRate,
        double uplinkDopplerSlopeHzPerSec,
        string? uplinkMode)
    {
        var sign = TxPrecompSign(uplinkMode);
        // removeLinearDrift(slope) applies −slope to audio frequency; we want
        // audio offset = sign · uplinkSlope · t, so pass slope = −sign · uplinkSlope.
        return RemoveLinearDrift(pcm, sampleRate, -sign * uplinkDopplerSlopeHzPerSec);
    }

    private static void FftInPlace(double[] re, double[] im, bool inverse)
    {
        var n = re.Length;
        var j = 0;
        for (var i = 1; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i >= j)
                continue;
            (re[i], re[j]) = (re[j], re[i]);
            (im[i], im[j]) = (im[j], im[i]);
        }

        var angSign = inverse ? 1.0 : -1.0;
        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = angSign * 2.0 * Math.PI / len;
            var wlenRe = Math.Cos(ang);
            var wlenIm = Math.Sin(ang);
            for (var i = 0; i < n; i += len)
            {
                var wRe = 1.0;
                var wIm = 0.0;
                var half = len >> 1;
                for (var k = 0; k < half; k++)
                {
                    var uRe = re[i + k];
                    var uIm = im[i + k];
                    var vRe = re[i + k + half] * wRe - im[i + k + half] * wIm;
                    var vIm = re[i + k + half] * wIm + im[i + k + half] * wRe;
                    re[i + k] = uRe + vRe;
                    im[i + k] = uIm + vIm;
                    re[i + k + half] = uRe - vRe;
                    im[i + k + half] = uIm - vIm;
                    var nextWRe = wRe * wlenRe - wIm * wlenIm;
                    wIm = wRe * wlenIm + wIm * wlenRe;
                    wRe = nextWRe;
                }
            }
        }
    }
}
