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
}
