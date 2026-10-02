using OscarWatch.Core.Models;
using OscarWatch.Core.Services;

namespace OscarWatch.Core.Ft4;

/// <summary>Maps an FT4 receive decode onto an OscarWatch.org satellite spot.</summary>
public static class Ft4OscarWatchSpots
{
    public const string Mode = "FT4";

    public static bool HasApiToken(string? apiToken) =>
        !string.IsNullOrWhiteSpace(apiToken);

    /// <summary>
    /// Spot for an ordinary receive decode. The reporter callsign and grid come from the
    /// OscarWatch account, so they are not set here. Own transmissions and own echoes are skipped.
    /// </summary>
    public static bool TryCreate(
        Ft4DecodedMessage decode,
        LiveTrackerSnapshot snapshot,
        string? myCallsign,
        string? client,
        out OscarWatchSatelliteSpot spot)
    {
        spot = null!;
        if (!decode.IsReceiveActivity)
            return false;

        var satellite = snapshot.SatelliteName?.Trim() ?? "";
        if (satellite.Length is 0 or > 64)
            return false;

        var call = Ft4MessageCodec.NormalizeCall(decode.CallDe ?? "");
        if (!Ft4PskReporterSpots.IsPlausibleCallsign(call))
            return false;

        var mine = Ft4MessageCodec.NormalizeCall(myCallsign ?? "");
        if (mine.Length > 0 && call.Equals(mine, StringComparison.Ordinal))
            return false;

        var heardAt = decode.SlotUtc.Kind switch
        {
            DateTimeKind.Utc => decode.SlotUtc,
            DateTimeKind.Local => decode.SlotUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(decode.SlotUtc, DateTimeKind.Utc)
        };

        var snr = float.IsFinite(decode.SnrDb) ? (int)Math.Round(decode.SnrDb) : 0;
        spot = new OscarWatchSatelliteSpot(
            satellite,
            call,
            TryHeardGrid(decode.Extra),
            Math.Clamp(snr, -50, 50),
            FrequencyOrNull(snapshot.UplinkHz),
            FrequencyOrNull(snapshot.DownlinkHz),
            heardAt,
            TrimTo(decode.Text, 64),
            TrimTo(client, 128));
        return true;
    }

    private static long? FrequencyOrNull(long hz) => hz > 0 ? hz : null;

    private static string? TryHeardGrid(string? extra)
    {
        if (string.IsNullOrWhiteSpace(extra) || Ft4MessageCodec.IsRr73(extra))
            return null;

        var grid = extra.Trim();
        if (grid.Length is not (4 or 6) || !Ft4MessageCodec.IsGrid(grid))
            return null;
        if (grid.Length == 6 && !(char.IsLetter(grid[4]) && char.IsLetter(grid[5])))
            return null;

        return grid.Length == 6
            ? grid[..4].ToUpperInvariant() + grid[4..].ToLowerInvariant()
            : grid.ToUpperInvariant();
    }

    private static string? TrimTo(string? value, int max)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0)
            return null;
        return text.Length <= max ? text : text[..max];
    }
}
