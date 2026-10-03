using OscarWatch.Core.Ft4;

namespace OscarWatch.Tests.Ft4;

public sealed class Ft4CallDtTests
{
    [Fact]
    public void Hint_at_a_different_delay_from_the_same_station_is_rejected()
    {
        var timing = new Ft4CallDt();
        timing.NoteReliable("ON8NT", 0.4, -12);

        Assert.False(timing.AllowsHint("ON8NT", -0.5));
        Assert.True(timing.AllowsHint("ON8NT", 0.4));
        Assert.True(timing.AllowsHint("EA3EA", -0.5));
    }

    [Fact]
    public void Snr_floor_does_not_set_the_station_delay()
    {
        var timing = new Ft4CallDt();
        timing.NoteReliable("ON8NT", 0.4, Ft4DecodeDepth.ApSnrFloorDb);

        Assert.True(timing.AllowsHint("ON8NT", 0.4));
    }
}
