using System.Globalization;
using OscarWatch.Core.Models;

namespace OscarWatch.Core.SatelliteLink;

public static class SatelliteLinkPassAlertMessageBuilder
{
    public static SatelliteLinkPassAlertMessage Build(PassInfo pass, DateTime timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(pass);

        var now = timestampUtc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc)
            : timestampUtc.ToUniversalTime();
        var aos = pass.AosUtc.ToUniversalTime();
        var seconds = (int)Math.Floor((aos - now).TotalSeconds);

        return new SatelliteLinkPassAlertMessage
        {
            TimestampUtc = FormatTimestamp(now),
            Pass = new SatelliteLinkPassAlertInfo
            {
                Satellite = new SatelliteLinkPassAlertSatelliteInfo
                {
                    Name = pass.SatelliteName.Trim(),
                    NoradId = pass.NoradId.Trim()
                },
                AosUtc = FormatTimestamp(aos),
                LosUtc = FormatTimestamp(pass.LosUtc),
                MaxElevationDeg = pass.MaxElevationDeg,
                SecondsUntilAos = Math.Max(0, seconds)
            }
        };
    }

    private static string FormatTimestamp(DateTime timestampUtc) =>
        timestampUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
