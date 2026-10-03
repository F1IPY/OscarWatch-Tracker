namespace OscarWatch.Core.Ft4;

/// <summary>
/// Remembers the DT of a station from a normal decode. A hinted reply whose
/// delay does not match is the same station read a second early or late.
/// </summary>
public sealed class Ft4CallDt
{
    /// <summary>One station stays inside this of its own earlier copy.</summary>
    public const double MaxDriftSec = 0.5;

    private readonly Dictionary<string, double> _dt = new(StringComparer.OrdinalIgnoreCase);

    public void Clear() => _dt.Clear();

    /// <summary>Learn from a CRC decode that is above the SNR floor.</summary>
    public void NoteReliable(string? callDe, double timeSec, float snrDb)
    {
        if (string.IsNullOrWhiteSpace(callDe))
            return;
        if (snrDb <= Ft4DecodeDepth.ApSnrFloorDb)
            return;
        if (!Ft4DecodeDepth.IsPlausibleSatelliteDt(timeSec))
            return;

        _dt[callDe] = timeSec;
    }

    public bool AllowsHint(string? callDe, double timeSec)
    {
        if (string.IsNullOrWhiteSpace(callDe))
            return true;
        if (!_dt.TryGetValue(callDe, out var known))
            return true;

        return Math.Abs(known - timeSec) <= MaxDriftSec;
    }
}
