using System.Diagnostics;
using OscarWatch.Core.Ft4;

namespace OscarWatch.Tests.Ft4;

[Collection(Ft4ClockCollection.Name)]
public sealed class Ft4RfPowerLeadTests
{
    [Fact]
    public void Read_starts_300_ms_before_the_slot()
    {
        var slot = new DateTime(2026, 10, 6, 12, 0, 7, 500, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 10, 6, 12, 0, 7, 200, DateTimeKind.Utc), Ft4RfPowerLead.CheckAtUtc(slot));
    }

    [Fact]
    public void Scheduler_waking_inside_the_lead_window_starts_the_read_at_once()
    {
        var slot = DateTime.UtcNow.AddMilliseconds(100);
        var sw = Stopwatch.StartNew();
        Ft4SlotWait.UntilUtc(Ft4RfPowerLead.CheckAtUtc(slot));
        Assert.True(sw.ElapsedMilliseconds < 20, $"waited {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Slow_verdict_holds_the_slot_until_it_answers()
    {
        var read = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = Task.Run(() => Ft4RfPowerLead.AwaitVerdict(read.Task));

        await Task.Delay(100);
        Assert.False(waiting.IsCompleted);

        read.SetResult(false);
        Assert.False(await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Passing_verdict_allows_the_slot()
    {
        Assert.True(Ft4RfPowerLead.AwaitVerdict(Task.FromResult(true)));
    }
}
