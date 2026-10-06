namespace OscarWatch.Core.Ft4;

public enum Ft4RxHealthKind
{
    NoAudio,
    AudioResumed,
    Silent,
    SoundResumed,
    NoDecodes,
    DecodesResumed,
}

/// <param name="Slots">Receive slots in the run that raised or cleared the condition.</param>
/// <param name="Gap">How long the capture went without audio (no-audio events only).</param>
public readonly record struct Ft4RxHealthEvent(
    Ft4RxHealthKind Kind,
    int Slots = 0,
    double LevelDbfs = 0,
    double? ElevationDeg = null,
    TimeSpan Gap = default);

/// <summary>
/// Notices an FT4 receive path that has stopped working: no audio from the soundcard,
/// audio that is digital silence, or nothing decoded for minutes with the satellite well up.
/// Raises one event when a condition starts and one when it clears, never per slot.
/// </summary>
public sealed class Ft4RxHealth
{
    public static readonly TimeSpan NoAudioAfter = TimeSpan.FromSeconds(3);

    /// <summary>Below this the capture is a muted or dead device, not a quiet band.</summary>
    public const double SilentDbfs = -90;

    public const int SilentSlots = 4;

    /// <summary>Under this elevation an empty passband is normal, so empty slots are not counted.</summary>
    public const double NoDecodesMinElevationDeg = 15;

    /// <summary>40 receive slots is five minutes of listening.</summary>
    public const int NoDecodesSlots = 40;

    private static readonly Ft4RxHealthEvent[] None = [];

    private readonly object _gate = new();
    private DateTime _lastAudioUtc;
    private bool _noAudio;
    private DateTime _lastSlot = DateTime.MinValue;
    private int _silentRun;
    private bool _silent;
    private int _emptyRun;
    private bool _noDecodes;

    public Ft4RxHealth(DateTime utcNow) => Reset(utcNow);

    public void Reset(DateTime utcNow)
    {
        lock (_gate)
        {
            _lastAudioUtc = utcNow;
            _noAudio = false;
            _lastSlot = DateTime.MinValue;
            _silentRun = 0;
            _silent = false;
            _emptyRun = 0;
            _noDecodes = false;
        }
    }

    /// <summary>Call on every capture read.</summary>
    public Ft4RxHealthEvent? OnCapture(DateTime utcNow, bool gotSamples)
    {
        lock (_gate)
        {
            var gap = utcNow - _lastAudioUtc;
            if (gotSamples)
            {
                _lastAudioUtc = utcNow;
                if (!_noAudio)
                    return null;
                _noAudio = false;
                return new Ft4RxHealthEvent(Ft4RxHealthKind.AudioResumed, Gap: gap);
            }

            if (_noAudio || gap < NoAudioAfter)
                return null;
            _noAudio = true;
            return new Ft4RxHealthEvent(Ft4RxHealthKind.NoAudio, Gap: gap);
        }
    }

    /// <summary>
    /// Call after each receive (not transmit) slot decode. A second decode of the same slot
    /// only counts if it found something the first missed.
    /// </summary>
    public IReadOnlyList<Ft4RxHealthEvent> OnReceiveSlot(
        DateTime slotStart,
        double levelDbfs,
        int decodes,
        double? elevationDeg)
    {
        lock (_gate)
        {
            if (slotStart < _lastSlot)
                return None;

            if (slotStart == _lastSlot)
                return decodes > 0 ? ClearNoDecodes(levelDbfs, elevationDeg) : None;

            _lastSlot = slotStart;
            List<Ft4RxHealthEvent>? events = null;

            if (levelDbfs < SilentDbfs)
            {
                _silentRun++;
                if (!_silent && _silentRun >= SilentSlots)
                {
                    _silent = true;
                    return [new Ft4RxHealthEvent(Ft4RxHealthKind.Silent, _silentRun, levelDbfs, elevationDeg)];
                }

                return None;
            }

            if (_silent)
            {
                events = [new Ft4RxHealthEvent(Ft4RxHealthKind.SoundResumed, _silentRun, levelDbfs, elevationDeg)];
                _silent = false;
            }

            _silentRun = 0;

            if (decodes > 0)
            {
                var cleared = ClearNoDecodes(levelDbfs, elevationDeg);
                if (cleared.Count > 0)
                    (events ??= []).AddRange(cleared);
            }
            else if (elevationDeg >= NoDecodesMinElevationDeg)
            {
                _emptyRun++;
                if (!_noDecodes && _emptyRun >= NoDecodesSlots)
                {
                    _noDecodes = true;
                    (events ??= []).Add(new Ft4RxHealthEvent(Ft4RxHealthKind.NoDecodes, _emptyRun, levelDbfs, elevationDeg));
                }
            }
            else
            {
                // Satellite low or set: an empty passband says nothing about the receive path.
                _emptyRun = 0;
                _noDecodes = false;
            }

            return events is null ? None : events;
        }
    }

    private IReadOnlyList<Ft4RxHealthEvent> ClearNoDecodes(double levelDbfs, double? elevationDeg)
    {
        var run = _emptyRun;
        _emptyRun = 0;
        if (!_noDecodes)
            return None;
        _noDecodes = false;
        return [new Ft4RxHealthEvent(Ft4RxHealthKind.DecodesResumed, run, levelDbfs, elevationDeg)];
    }

    /// <summary>RMS level in dBFS; digital silence reports −150.</summary>
    public static double LevelDbfs(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
            return -150;
        double sum = 0;
        foreach (var s in samples)
            sum += (double)s * s;
        var meanSquare = sum / samples.Length;
        return meanSquare <= 1e-15 ? -150 : 10 * Math.Log10(meanSquare);
    }
}
