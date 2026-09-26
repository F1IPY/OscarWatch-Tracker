namespace OscarWatch.Core.Ft4;

/// <summary>Picks which copy of our own echo to show when the decoder reports it twice.</summary>
public static class Ft4EchoChoice
{
    /// <summary>
    /// True when <paramref name="candidateHz"/> should replace an echo already shown for this transmission.
    /// The copy nearer the TX marker wins. A higher SNR wins when they are equally close.
    /// </summary>
    public static bool IsClearerCopy(
        float shownHz,
        float shownSnr,
        float candidateHz,
        float candidateSnr,
        double txHz)
    {
        var shownError = Math.Abs(shownHz - txHz);
        var candidateError = Math.Abs(candidateHz - txHz);
        const double tieHz = 2.0;
        if (candidateError + tieHz < shownError)
            return true;
        if (shownError + tieHz < candidateError)
            return false;
        return candidateSnr > shownSnr + 0.5f;
    }
}
