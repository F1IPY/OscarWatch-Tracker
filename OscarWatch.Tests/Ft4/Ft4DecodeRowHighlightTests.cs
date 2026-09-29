using OscarWatch.Core.Ft4;
using OscarWatch.ViewModels;

namespace OscarWatch.Tests.Ft4;

public sealed class Ft4DecodeRowHighlightTests
{
    private static Ft4DecodedMessage Line(string text, string? callTo, string? callDe, string? extra = null) =>
        new(DateTime.UtcNow, text, 1200, 0.2f, 10, callTo, callDe, extra, false);

    [Fact]
    public void Same_colour_keeps_the_brush()
    {
        var row = new Ft4DecodeRowViewModel(Line("CQ G4ABC IO91", "CQ", "G4ABC", "IO91"));
        var worked = new HashSet<string>(StringComparer.Ordinal);
        var grids = new HashSet<string>(StringComparer.Ordinal);

        row.RefreshHighlight("MM9SQL", null, "#66E6B15A", "#665CB88A", "#664D9DE8", "#66C07AD0", "#6678C8E0", worked, grids);
        var first = row.RowBackground;
        row.RefreshHighlight("MM9SQL", null, "#66E6B15A", "#665CB88A", "#664D9DE8", "#66C07AD0", "#6678C8E0", worked, grids);

        Assert.Same(first, row.RowBackground);
    }

    [Fact]
    public void Becoming_the_partner_replaces_the_brush()
    {
        var row = new Ft4DecodeRowViewModel(Line("MM9SQL G4ABC IO91", "MM9SQL", "G4ABC", "IO91"));

        row.RefreshHighlight("MM9SQL", null, "#66E6B15A", "#665CB88A", "#664D9DE8", "#66C07AD0", "#6678C8E0", null, null);
        var calling = row.RowBackground;
        row.RefreshHighlight("MM9SQL", "G4ABC", "#66E6B15A", "#665CB88A", "#664D9DE8", "#66C07AD0", "#6678C8E0", null, null);

        Assert.NotSame(calling, row.RowBackground);
    }
}
