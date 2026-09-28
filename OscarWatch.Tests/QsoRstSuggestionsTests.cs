using OscarWatch.Core.Logbook;

namespace OscarWatch.Tests;

public class QsoRstSuggestionsTests
{
    [Theory]
    [InlineData("USB", "FT4", true)]
    [InlineData("FT4", "", true)]
    [InlineData("usb", "ft8", true)]
    [InlineData("USB", "SSB", false)]
    [InlineData("FM", "", false)]
    public void UsesSnr_follows_ft4_and_ft8(string mode, string modeType, bool expected) =>
        Assert.Equal(expected, QsoRstSuggestions.UsesSnr(mode, modeType));

    [Fact]
    public void DefaultReport_uses_signal_reports_for_ft4_even_when_the_uplink_is_usb()
    {
        Assert.Equal("+00", QsoRstSuggestions.DefaultReport("USB", "FT4"));
        Assert.Equal("599", QsoRstSuggestions.DefaultReport("USB", "SSB"));
        Assert.Equal("59", QsoRstSuggestions.DefaultReport("FM", ""));
        Assert.Contains("+05", QsoRstSuggestions.SnrOptions);
        Assert.Contains("-12", QsoRstSuggestions.SnrOptions);
        Assert.DoesNotContain("59", QsoRstSuggestions.SnrOptions);
    }

    [Theory]
    [InlineData("R+05", true, "+05")]
    [InlineData("+5", true, "+05")]
    [InlineData("-8", true, "-08")]
    [InlineData("5", true, "+05")]
    [InlineData("59", true, "59")]
    [InlineData("R-12", false, "-12")]
    [InlineData("59", false, "59")]
    [InlineData("+05", false, "+05")]
    public void NormalizeForLog_stores_ft4_reports_as_signed_snr(string value, bool snrMode, string expected) =>
        Assert.Equal(expected, QsoRstSuggestions.NormalizeForLog(value, snrMode));
}
