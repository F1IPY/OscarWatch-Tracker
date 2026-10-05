namespace OscarWatch.Core.Ft4;

/// <summary>
/// Residual slope range for stations whose uplink still slides within their transmission.
/// Many rigs ignore CAT tuning while keyed, so a station without audio pre-comp
/// arrives with roughly its uplink Doppler slope left over after our downlink correction.
/// The sign depends on the transponder and their setup, so the native decoder searches
/// both sides of the steady track in one pass.
/// </summary>
public static class Ft4DriftSearch
{
    /// <summary>
    /// Native grid spacing. The decoder copes with about ±2 Hz/s between hypotheses,
    /// so 4 Hz/s steps leave no gap.
    /// </summary>
    public const double StepHzPerSec = 4;

    /// <summary>Slide the steady search already decodes, so smaller slopes need no drift search.</summary>
    public const double SteadyToleranceHzPerSec = 4;

    /// <summary>Upper bound on steps each side, so a TCA slope cannot run away with CPU.</summary>
    public const int MaxStepsEachSide = 16;

    /// <summary>
    /// Largest leftover slope (Hz/s, either sign, relative to the downlink-corrected copy) worth
    /// searching. Covers our own uplink slope with some margin, since other stations see a
    /// different geometry. Zero when the uplink barely moves within a slot.
    /// </summary>
    public static double MaxResidualHzPerSec(double uplinkSlopeHzPerSec)
    {
        if (!double.IsFinite(uplinkSlopeHzPerSec))
            return 0;

        var span = Math.Abs(uplinkSlopeHzPerSec) * 1.3;
        if (span < SteadyToleranceHzPerSec)
            return 0;

        return Math.Min(span + StepHzPerSec / 2, StepHzPerSec * MaxStepsEachSide);
    }

    /// <summary>Drift hypotheses each side of steady for <see cref="MaxResidualHzPerSec"/>; 0 means steady only.</summary>
    public static int Steps(double uplinkSlopeHzPerSec)
    {
        var reach = MaxResidualHzPerSec(uplinkSlopeHzPerSec);
        if (reach <= 0)
            return 0;

        return Math.Clamp((int)Math.Ceiling(reach / StepHzPerSec), 1, MaxStepsEachSide);
    }
}
