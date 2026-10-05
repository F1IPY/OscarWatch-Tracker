namespace OscarWatch.Core.Ft4;

/// <summary>
/// Extra receive slopes for stations whose uplink still slides within their transmission.
/// Many rigs ignore CAT tuning while keyed, so a station without audio pre-comp
/// arrives with roughly its uplink Doppler slope left over after our downlink correction.
/// The sign depends on the transponder and their setup, so both sides are tried.
/// </summary>
public static class Ft4DriftSearch
{
    /// <summary>
    /// Grid spacing. The decoder copes with about ±4 Hz/s of leftover slide,
    /// so 8 Hz/s steps leave no gap between neighbouring passes.
    /// </summary>
    public const double StepHzPerSec = 8;

    /// <summary>Upper bound on steps each side, so a TCA slope cannot run away with CPU.</summary>
    public const int MaxStepsEachSide = 8;

    /// <summary>
    /// Leftover slopes (Hz/s, relative to the downlink-corrected copy) worth an extra pass.
    /// Covers our own uplink slope with some margin, since other stations see a different geometry.
    /// Empty when the uplink barely moves within a slot and the normal pass already covers it.
    /// </summary>
    public static IReadOnlyList<double> ResidualSlopes(double uplinkSlopeHzPerSec)
    {
        if (!double.IsFinite(uplinkSlopeHzPerSec))
            return [];

        var reach = Math.Abs(uplinkSlopeHzPerSec) * 1.3 + StepHzPerSec / 2;
        var steps = Math.Min(MaxStepsEachSide, (int)Math.Floor(reach / StepHzPerSec));
        if (steps < 1)
            return [];

        var slopes = new List<double>(steps * 2);
        for (var k = 1; k <= steps; k++)
        {
            slopes.Add(k * StepHzPerSec);
            slopes.Add(-k * StepHzPerSec);
        }

        return slopes;
    }
}
