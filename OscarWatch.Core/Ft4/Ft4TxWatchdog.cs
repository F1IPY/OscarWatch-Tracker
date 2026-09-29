namespace OscarWatch.Core.Ft4;

/// <summary>
/// Stops FT4 transmit when nobody has replied to this station for a set time.
/// Other stations on the passband, and this station's own echo, do not count.
/// </summary>
public static class Ft4TxWatchdog
{
    public const int DefaultMinutes = 3;
    public const int MaxMinutes = 30;

    /// <summary>0 disables the watchdog.</summary>
    public static int ClampMinutes(int minutes) => Math.Clamp(minutes, 0, MaxMinutes);

    public static bool ShouldHalt(
        bool transmitEnabled,
        bool tuning,
        int minutes,
        DateTime utcNow,
        DateTime lastReplyUtc)
    {
        if (tuning || !transmitEnabled || minutes <= 0)
            return false;

        return utcNow - lastReplyUtc > TimeSpan.FromMinutes(minutes);
    }

    /// <summary>True when another station addressed this callsign.</summary>
    public static bool IsReply(string? callTo, string? callDe, string? myCall)
    {
        if (string.IsNullOrWhiteSpace(myCall) || string.IsNullOrWhiteSpace(callDe))
            return false;
        if (callDe.Equals(myCall, StringComparison.OrdinalIgnoreCase))
            return false;
        return Ft4MessageCodec.IsAddressedTo(callTo, myCall);
    }
}
