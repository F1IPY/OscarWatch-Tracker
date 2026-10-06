using OscarWatch.Core.Ft4;

namespace OscarWatch.Tests.Ft4;

public sealed class Ft4RxHealthTests
{
    private static readonly DateTime Start = new(2026, 10, 6, 20, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Slot = TimeSpan.FromSeconds(7.5);

    [Fact]
    public void Missing_capture_audio_is_raised_once_and_cleared_once()
    {
        var health = new Ft4RxHealth(Start);

        Assert.Null(health.OnCapture(Start.AddSeconds(2.9), gotSamples: false));
        var stuck = health.OnCapture(Start.AddSeconds(3), gotSamples: false);
        Assert.Equal(Ft4RxHealthKind.NoAudio, stuck?.Kind);
        Assert.Null(health.OnCapture(Start.AddSeconds(10), gotSamples: false));

        var resumed = health.OnCapture(Start.AddSeconds(12), gotSamples: true);
        Assert.Equal(Ft4RxHealthKind.AudioResumed, resumed?.Kind);
        Assert.Equal(12, resumed!.Value.Gap.TotalSeconds, 3);
        Assert.Null(health.OnCapture(Start.AddSeconds(12.1), gotSamples: true));
    }

    [Fact]
    public void Steady_audio_never_raises_anything()
    {
        var health = new Ft4RxHealth(Start);
        for (var i = 1; i <= 1000; i++)
            Assert.Null(health.OnCapture(Start.AddMilliseconds(i * 15), gotSamples: true));
    }

    [Fact]
    public void Digital_silence_is_raised_after_four_slots_and_cleared_when_sound_returns()
    {
        var health = new Ft4RxHealth(Start);
        for (var i = 0; i < Ft4RxHealth.SilentSlots - 1; i++)
            Assert.Empty(health.OnReceiveSlot(Start + Slot * i, -150, 0, 40));

        var silent = Assert.Single(health.OnReceiveSlot(Start + Slot * 3, -150, 0, 40));
        Assert.Equal(Ft4RxHealthKind.Silent, silent.Kind);
        Assert.Empty(health.OnReceiveSlot(Start + Slot * 4, -150, 0, 40));

        var back = Assert.Single(health.OnReceiveSlot(Start + Slot * 5, -40, 0, 40));
        Assert.Equal(Ft4RxHealthKind.SoundResumed, back.Kind);
        Assert.Equal(5, back.Slots);
    }

    [Fact]
    public void Empty_slots_with_the_satellite_well_up_raise_no_decodes_once()
    {
        var health = new Ft4RxHealth(Start);
        for (var i = 0; i < Ft4RxHealth.NoDecodesSlots - 1; i++)
            Assert.Empty(health.OnReceiveSlot(Start + Slot * i, -40, 0, 30));

        var stuck = Assert.Single(health.OnReceiveSlot(Start + Slot * 39, -40, 0, 30));
        Assert.Equal(Ft4RxHealthKind.NoDecodes, stuck.Kind);
        Assert.Equal(30, stuck.ElevationDeg);
        Assert.Empty(health.OnReceiveSlot(Start + Slot * 40, -40, 0, 30));

        var resumed = Assert.Single(health.OnReceiveSlot(Start + Slot * 41, -40, 2, 30));
        Assert.Equal(Ft4RxHealthKind.DecodesResumed, resumed.Kind);
        Assert.Equal(41, resumed.Slots);
    }

    [Theory]
    [InlineData(10.0)]
    [InlineData(null)]
    public void Empty_slots_near_the_horizon_or_with_no_satellite_are_normal(double? elevation)
    {
        var health = new Ft4RxHealth(Start);
        for (var i = 0; i < 200; i++)
            Assert.Empty(health.OnReceiveSlot(Start + Slot * i, -40, 0, elevation));
    }

    [Fact]
    public void A_decode_resets_the_empty_run()
    {
        var health = new Ft4RxHealth(Start);
        for (var i = 0; i < 30; i++)
            health.OnReceiveSlot(Start + Slot * i, -40, 0, 30);
        health.OnReceiveSlot(Start + Slot * 30, -40, 1, 30);
        for (var i = 31; i < 31 + Ft4RxHealth.NoDecodesSlots - 1; i++)
            Assert.Empty(health.OnReceiveSlot(Start + Slot * i, -40, 0, 30));
    }

    [Fact]
    public void Second_decode_of_the_same_slot_only_counts_when_it_finds_something()
    {
        var health = new Ft4RxHealth(Start);
        for (var i = 0; i < Ft4RxHealth.NoDecodesSlots; i++)
            health.OnReceiveSlot(Start + Slot * i, -40, 0, 30);

        var last = Start + Slot * (Ft4RxHealth.NoDecodesSlots - 1);
        Assert.Empty(health.OnReceiveSlot(last, -40, 0, 30));
        var resumed = Assert.Single(health.OnReceiveSlot(last, -40, 1, 30));
        Assert.Equal(Ft4RxHealthKind.DecodesResumed, resumed.Kind);
    }

    [Fact]
    public void Reset_clears_every_condition()
    {
        var health = new Ft4RxHealth(Start);
        health.OnCapture(Start.AddSeconds(5), gotSamples: false);
        for (var i = 0; i < Ft4RxHealth.NoDecodesSlots; i++)
            health.OnReceiveSlot(Start + Slot * i, -40, 0, 30);

        var later = Start.AddMinutes(10);
        health.Reset(later);
        Assert.Null(health.OnCapture(later.AddSeconds(1), gotSamples: true));
        Assert.Empty(health.OnReceiveSlot(later, -40, 3, 30));
    }

    [Fact]
    public void Level_reports_digital_silence_and_full_scale()
    {
        Assert.Equal(-150, Ft4RxHealth.LevelDbfs(new float[1200]));
        Assert.Equal(-150, Ft4RxHealth.LevelDbfs([]));
        Assert.Equal(0, Ft4RxHealth.LevelDbfs([1f, -1f, 1f, -1f]), 6);
        Assert.Equal(-40, Ft4RxHealth.LevelDbfs([0.01f, -0.01f]), 6);
    }
}
