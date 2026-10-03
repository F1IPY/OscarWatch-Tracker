using OscarWatch.Core.Ft4;

namespace OscarWatch.Tests.Ft4;

public sealed class Ft4ActivityLogTests
{
    [Fact]
    public void FormatAll_writes_oldest_first_with_tx_marker()
    {
        var older = new Ft4DecodedMessage(
            new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc),
            "CQ G4ABC JO01",
            1200f,
            0.2f,
            -8f,
            "CQ",
            "G4ABC",
            "JO01",
            IsOwnEcho: false);
        var newerTx = new Ft4DecodedMessage(
            new DateTime(2026, 9, 21, 12, 0, 7, DateTimeKind.Utc),
            "G4ABC MM9SQL IO85",
            1500f,
            0f,
            0f,
            "G4ABC",
            "MM9SQL",
            "IO85",
            IsOwnEcho: false,
            IsTransmitted: true);

        // UI list is newest-first; FormatAll should still emit chronological order.
        var text = Ft4ActivityLog.FormatAll([newerTx, older]);

        Assert.Contains("CQ G4ABC JO01", text);
        Assert.Contains("G4ABC MM9SQL IO85", text);
        var hinted = new Ft4DecodedMessage(
            new DateTime(2026, 9, 21, 12, 0, 15, DateTimeKind.Utc),
            "MM9SQL G4ABC R+35",
            1500f,
            -0.5f,
            -21f,
            "MM9SQL",
            "G4ABC",
            "R+35",
            IsOwnEcho: false,
            IsApriori: true);
        text = Ft4ActivityLog.FormatAll([newerTx, older, hinted]);

        Assert.Contains(" TX ", text);
        Assert.Contains(" AP ", text);
        Assert.Contains(" RX ", text);
        var cqIndex = text.IndexOf("CQ G4ABC JO01", StringComparison.Ordinal);
        var txIndex = text.IndexOf("G4ABC MM9SQL IO85", StringComparison.Ordinal);
        Assert.True(cqIndex < txIndex);
    }
}
