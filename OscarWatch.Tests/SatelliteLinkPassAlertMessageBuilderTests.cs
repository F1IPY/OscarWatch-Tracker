using OscarWatch.Core.Models;
using OscarWatch.Core.SatelliteLink;

namespace OscarWatch.Tests;

public class SatelliteLinkPassAlertMessageBuilderTests
{
    [Fact]
    public void Build_maps_scheduled_pass_without_frequencies()
    {
        var pass = new PassInfo
        {
            SatelliteName = " AO-07 ",
            NoradId = "07530",
            AosUtc = new DateTime(2026, 10, 7, 18, 30, 0, DateTimeKind.Utc),
            LosUtc = new DateTime(2026, 10, 7, 18, 42, 0, DateTimeKind.Utc),
            MaxElevationDeg = 24.6,
            MaxElevationUtc = new DateTime(2026, 10, 7, 18, 36, 0, DateTimeKind.Utc)
        };

        var msg = SatelliteLinkPassAlertMessageBuilder.Build(
            pass,
            new DateTime(2026, 10, 7, 18, 25, 0, DateTimeKind.Utc));

        Assert.Equal("passAlert", msg.Type);
        Assert.Equal(1, msg.Version);
        Assert.Equal("2026-10-07T18:25:00.000Z", msg.TimestampUtc);
        Assert.Equal("AO-07", msg.Pass!.Satellite!.Name);
        Assert.Equal("07530", msg.Pass.Satellite.NoradId);
        Assert.Equal("2026-10-07T18:30:00.000Z", msg.Pass.AosUtc);
        Assert.Equal("2026-10-07T18:42:00.000Z", msg.Pass.LosUtc);
        Assert.Equal(24.6, msg.Pass.MaxElevationDeg);
        Assert.Equal(300, msg.Pass.SecondsUntilAos);
    }

    [Fact]
    public void Build_clamps_seconds_until_aos_at_zero()
    {
        var pass = new PassInfo
        {
            SatelliteName = "SO-50",
            NoradId = "27607",
            AosUtc = new DateTime(2026, 10, 7, 18, 30, 0, DateTimeKind.Utc),
            LosUtc = new DateTime(2026, 10, 7, 18, 42, 0, DateTimeKind.Utc),
            MaxElevationDeg = 12
        };

        var msg = SatelliteLinkPassAlertMessageBuilder.Build(
            pass,
            new DateTime(2026, 10, 7, 18, 31, 0, DateTimeKind.Utc));

        Assert.Equal(0, msg.Pass!.SecondsUntilAos);
    }
}
