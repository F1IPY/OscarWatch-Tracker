namespace OscarWatch.Core.Ft4;

/// <summary>
/// Deep FT4 decode near the horizon, where the downlink is weakest.
/// Mid-pass stays on the fast budget so a reply still makes the next slot.
/// </summary>
public static class Ft4DecodeDepth
{
    /// <summary>Deep decode while the satellite is up and below this elevation.</summary>
    public const double HorizonElevationDeg = 20;

    public static bool UseDeep(double? elevationDeg) =>
        elevationDeg is >= 0 and < HorizonElevationDeg;

    /// <summary>Also decode the full receive slot while the satellite is up and below this elevation.</summary>
    public const double FullSlotElevationDeg = 5;

    /// <summary>
    /// The first few degrees: a weak or slightly late burst can miss the 6 s early decode,
    /// so receive slots get a second pass over the whole 7.5 s capture.
    /// </summary>
    public static bool UseFullSlotDecode(double? elevationDeg) =>
        elevationDeg is >= 0 and < FullSlotElevationDeg;

    /// <summary>
    /// Hinted replies are only tried while the satellite is still up.
    /// Below the horizon those same hints turn noise into a plausible report.
    /// </summary>
    public static bool UseApriori(double? elevationDeg) =>
        elevationDeg is >= 0;

    /// <summary>
    /// Earliest DT still treated as a satellite copy. The native search starts
    /// about half a second before the slot.
    /// </summary>
    public const double MinSatelliteDtSec = -0.5;

    /// <summary>
    /// Latest DT still treated as a satellite copy. FT4 starts 0.5 s into the
    /// slot and a satellite adds only milliseconds, so a real line stays near
    /// that. A moonbounce echo is about +2.5 s.
    /// </summary>
    public const double MaxSatelliteDtSec = 1.5;

    public static bool IsPlausibleSatelliteDt(double timeSec) =>
        timeSec is >= MinSatelliteDtSec and <= MaxSatelliteDtSec;
}
