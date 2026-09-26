using OscarWatch.Core.Ft4;

namespace OscarWatch.Tests.Ft4;

public sealed class Ft4EchoChoiceTests
{
    [Fact]
    public void Copy_on_the_tx_marker_replaces_one_10_hz_off()
    {
        Assert.True(Ft4EchoChoice.IsClearerCopy(
            shownHz: 1240f, shownSnr: 9f,
            candidateHz: 1250f, candidateSnr: 15f,
            txHz: 1250));
    }

    [Fact]
    public void Copy_10_hz_off_does_not_replace_the_one_on_the_marker()
    {
        Assert.False(Ft4EchoChoice.IsClearerCopy(
            shownHz: 1250f, shownSnr: 15f,
            candidateHz: 1240f, candidateSnr: 9f,
            txHz: 1250));
    }

    [Fact]
    public void Higher_snr_replaces_an_equally_close_copy()
    {
        Assert.True(Ft4EchoChoice.IsClearerCopy(
            shownHz: 1250f, shownSnr: 9f,
            candidateHz: 1251f, candidateSnr: 15f,
            txHz: 1250));
    }

    [Fact]
    public void Same_copy_is_not_treated_as_clearer()
    {
        Assert.False(Ft4EchoChoice.IsClearerCopy(
            shownHz: 1250f, shownSnr: 15f,
            candidateHz: 1250f, candidateSnr: 15f,
            txHz: 1250));
    }
}
