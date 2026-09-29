namespace OscarWatch.Core.Ft4;

public enum Ft4QsoPhase
{
    Idle,
    CallingCq,
    InQso,
    Finished
}

/// <summary>Auto-sequences standard satellite FT4 exchanges.</summary>
public sealed class Ft4QsoSequencer
{
    private readonly Func<string> _myCall;
    private readonly Func<string> _myGrid;
    private readonly Func<bool> _skipRrr;
    private readonly Func<bool> _holdTxFrequency;
    private readonly Func<bool> _autoReply;
    private readonly object _gate = new();
    private bool? _autoReplyOverride;

    public Ft4QsoSequencer(
        Func<string> myCall,
        Func<string> myGrid,
        Func<bool> skipRrr,
        Func<bool>? holdTxFrequency = null,
        Func<bool>? autoReply = null)
    {
        _myCall = myCall;
        _myGrid = myGrid;
        _skipRrr = skipRrr;
        _holdTxFrequency = holdTxFrequency ?? (() => true);
        _autoReply = autoReply ?? (() => true);
    }

    /// <summary>
    /// The Auto reply checkbox. Overrides the settings read so a tick takes effect
    /// on the next decode, including one already on screen.
    /// </summary>
    public void SetAutoReply(bool enabled)
    {
        lock (_gate)
            _autoReplyOverride = enabled;
    }

    private bool AutoReplyOn => _autoReplyOverride ?? _autoReply();

    public bool IsCqMessage =>
        Ft4MessageCodec.TryParse(CurrentTxMessage, out var callTo, out _, out _)
        && Ft4MessageCodec.IsCq(callTo);

    public Ft4QsoPhase Phase { get; private set; } = Ft4QsoPhase.Idle;
    public string? TheirCall { get; private set; }
    public string? TheirGrid { get; private set; }
    public string? ReportSent { get; private set; }
    public string? ReportReceived { get; private set; }
    public string CurrentTxMessage { get; private set; } = "";
    public bool TransmitEnabled { get; private set; }

    /// <summary>
    /// UTC when the last contact reached 73. Decodes at or before this belong to that
    /// contact, so a new CQ must not answer them again.
    /// </summary>
    public DateTime? QsoCompletedUtc { get; private set; }
    public bool PreferEvenSlot { get; set; }
    public double TxAudioHz { get; set; } = 1500;

    public void Reset()
    {
        lock (_gate)
            ResetCore();
    }

    private void ResetCore()
    {
        Phase = Ft4QsoPhase.Idle;
        TheirCall = null;
        TheirGrid = null;
        ReportSent = null;
        ReportReceived = null;
        CurrentTxMessage = "";
        TransmitEnabled = false;
        QsoCompletedUtc = null;
    }

    /// <summary>True when <paramref name="slotUtc"/> is part of the contact that just finished.</summary>
    public bool IsHistoricDecode(DateTime slotUtc) =>
        QsoCompletedUtc is not null && slotUtc <= QsoCompletedUtc.Value;

    public void StartCq(bool evenSlot)
    {
        // Decode and TX-complete run off the UI thread. Without the lock a 73 that
        // already passed its phase check can write the old contact back over this CQ.
        lock (_gate)
        {
            PreferEvenSlot = evenSlot;
            CurrentTxMessage = Ft4MessageCodec.BuildCq(_myCall(), _myGrid());
            BeginFreshCq(keepMessage: true);
        }
    }

    /// <summary>Operator clicked a decode to answer.</summary>
    public void StartAnswer(Ft4DecodedMessage decode, bool oppositeEvenSlot)
    {
        lock (_gate)
            StartAnswerCore(decode, oppositeEvenSlot);
    }

    private void StartAnswerCore(Ft4DecodedMessage decode, bool oppositeEvenSlot)
    {
        if (!Ft4MessageCodec.TryParse(decode.Text, out var callTo, out var callDe, out var extra)
            || string.IsNullOrWhiteSpace(callDe))
            return;

        var my = _myCall();
        // Ignore own echoes / own TX lines in the decode list.
        if (callDe.Equals(my, StringComparison.OrdinalIgnoreCase))
            return;

        PreferEvenSlot = oppositeEvenSlot;
        if (!_holdTxFrequency())
            TxAudioHz = decode.FreqHz;
        TheirCall = Ft4MessageCodec.NormalizeCall(callDe);
        TheirGrid = Ft4MessageCodec.IsGrid(extra) ? extra : TheirGrid;
        Phase = Ft4QsoPhase.InQso;
        TransmitEnabled = true;
        ReportSent = null;
        ReportReceived = null;

        if (Ft4MessageCodec.IsCq(callTo))
        {
            // Standard first reply to a CQ is our grid, not a report.
            CurrentTxMessage = Ft4MessageCodec.BuildGridReply(TheirCall, my, _myGrid());
            return;
        }

        if (Ft4MessageCodec.IsAddressedTo(callTo, my))
        {
            if (Ft4MessageCodec.IsReport(extra))
            {
                ReportReceived = Ft4MessageCodec.NormalizeSnrReport(extra);
                ReportSent = Ft4MessageCodec.FormatSnrReport(decode.SnrDb);
                // A plain report still needs ours back (R+NN); only an R+NN is ready for RR73.
                CurrentTxMessage = !Ft4MessageCodec.IsRogerReport(extra)
                    ? Ft4MessageCodec.BuildReport(TheirCall, my, Ft4MessageCodec.FormatRogerReport(decode.SnrDb))
                    : _skipRrr()
                        ? Ft4MessageCodec.BuildRr73(TheirCall, my)
                        : Ft4MessageCodec.BuildRrr(TheirCall, my);
                return;
            }

            if (Ft4MessageCodec.IsGrid(extra))
            {
                TheirGrid = extra;
                ReportSent = Ft4MessageCodec.FormatSnrReport(decode.SnrDb);
                CurrentTxMessage = Ft4MessageCodec.BuildReport(TheirCall, my, ReportSent);
                return;
            }

            if (Ft4MessageCodec.IsClosing(extra))
            {
                CurrentTxMessage = Ft4MessageCodec.Build73(TheirCall, my);
                return;
            }
        }

        CurrentTxMessage = Ft4MessageCodec.BuildGridReply(TheirCall, my, _myGrid());
    }

    public void HaltTx()
    {
        lock (_gate)
        {
            TransmitEnabled = false;
            if (Phase == Ft4QsoPhase.CallingCq)
                Phase = Ft4QsoPhase.Idle;
        }
    }

    public void EnableTx()
    {
        lock (_gate)
            EnableTxCore();
    }

    private void EnableTxCore()
    {
        if (string.IsNullOrWhiteSpace(CurrentTxMessage))
            CurrentTxMessage = Ft4MessageCodec.BuildCq(_myCall(), _myGrid());

        // A CQ in the box is a new call. Leaving the previous contact in place
        // meant auto reply ignored the station that answered this CQ.
        if (IsCqMessage)
        {
            BeginFreshCq(keepMessage: true);
            return;
        }

        if (Phase == Ft4QsoPhase.Idle || Phase == Ft4QsoPhase.Finished)
            Phase = Ft4QsoPhase.CallingCq;
        TransmitEnabled = true;
    }

    /// <summary>
    /// Auto reply was turned on while a CQ is going out. Drop any previous contact
    /// so the next caller is answered. Returns false when this is not a CQ.
    /// </summary>
    public bool PrepareAutoReply()
    {
        lock (_gate)
            return PrepareAutoReplyCore();
    }

    private bool PrepareAutoReplyCore()
    {
        if (!TransmitEnabled || !AutoReplyOn || !IsCqMessage)
            return false;

        if (Phase != Ft4QsoPhase.CallingCq || TheirCall is not null || ReportSent is not null)
            BeginFreshCq(keepMessage: true);
        return true;
    }

    private void BeginFreshCq(bool keepMessage)
    {
        Phase = Ft4QsoPhase.CallingCq;
        TheirCall = null;
        TheirGrid = null;
        ReportSent = null;
        ReportReceived = null;
        if (!keepMessage || string.IsNullOrWhiteSpace(CurrentTxMessage))
            CurrentTxMessage = Ft4MessageCodec.BuildCq(_myCall(), _myGrid());
        TransmitEnabled = true;
    }

    /// <summary>Operator-edited TX text from the FT4 window.</summary>
    public void SetTxMessage(string? message)
    {
        lock (_gate)
            SetTxMessageCore(message);
    }

    private void SetTxMessageCore(string? message)
    {
        var text = (message ?? "").Trim()
            .Replace('\u2215', '/')
            .Replace('\u2044', '/')
            .ToUpperInvariant();
        CurrentTxMessage = text;
    }

    /// <summary>
    /// Process a decode during our receive period. Returns true when the contact is complete
    /// and ready to log.
    /// </summary>
    public bool OnDecoded(Ft4DecodedMessage decode)
    {
        lock (_gate)
            return OnDecodedCore(decode);
    }

    private bool OnDecodedCore(Ft4DecodedMessage decode)
    {
        if (!TransmitEnabled
            && Phase is not Ft4QsoPhase.InQso and not Ft4QsoPhase.CallingCq and not Ft4QsoPhase.Finished)
            return false;

        if (!Ft4MessageCodec.TryParse(decode.Text, out var callTo, out var callDe, out var extra)
            || string.IsNullOrWhiteSpace(callDe))
            return false;

        var my = _myCall();

        if (Phase == Ft4QsoPhase.Finished)
        {
            // They missed RR73 and are still sending a report. Send it again.
            ResumeClosingIfTheyRepeatReport(callTo, callDe, extra, my);
            return false;
        }

        if (Phase == Ft4QsoPhase.CallingCq
            && Ft4MessageCodec.IsAddressedTo(callTo, my)
            && !callDe.Equals(my, StringComparison.OrdinalIgnoreCase))
        {
            // Auto reply off: keep calling CQ until the operator clicks a station.
            if (!AutoReplyOn)
                return false;

            // A 73 or RR73 is the end of a contact, not a station calling this CQ.
            if (Ft4MessageCodec.IsClosing(extra))
                return false;

            TheirCall = Ft4MessageCodec.NormalizeCall(callDe);
            TheirGrid = Ft4MessageCodec.IsGrid(extra) ? extra : TheirGrid;
            // Stay on our CQ frequency (WSJT-X). Only answering a decode moves TX.
            // Keep our CQ slot parity. The caller answered on the opposite slot; flipping
            // would put both stations on the same TX slots (WSJT-X then cannot decode us).
            Phase = Ft4QsoPhase.InQso;

            if (Ft4MessageCodec.IsGrid(extra) || string.IsNullOrWhiteSpace(extra))
            {
                ReportSent = Ft4MessageCodec.FormatSnrReport(decode.SnrDb);
                CurrentTxMessage = Ft4MessageCodec.BuildReport(TheirCall, my, ReportSent);
            }
            else if (Ft4MessageCodec.IsReport(extra))
            {
                // A plain +NN while we are calling CQ is their first report to us, often
                // because they are answering a call we did not take. Reply R+NN. Only an
                // R+NN from them is ready for RR73.
                ReplyToReport(TheirCall, my, extra, decode.SnrDb);
            }
            return false;
        }

        if (Phase != Ft4QsoPhase.InQso || TheirCall is null)
            return false;

        if (!callDe.Equals(TheirCall, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!Ft4MessageCodec.IsAddressedTo(callTo, my) && !Ft4MessageCodec.IsCq(callTo))
            return false;

        // Still calling CQ: remember their grid, but do not skip our pending grid reply.
        if (Ft4MessageCodec.IsCq(callTo))
        {
            if (Ft4MessageCodec.IsGrid(extra))
                TheirGrid = extra;
            return false;
        }

        if (_skipRrr() && Ft4MessageCodec.Is73(extra))
        {
            // Their 73 after our RR73: contact done, no further TX.
            FinishContact();
            return CanLog();
        }

        if (Ft4MessageCodec.IsRrr(extra) || Ft4MessageCodec.IsRr73(extra) || Ft4MessageCodec.Is73(extra))
        {
            if (CurrentTxMessage.EndsWith(" RR73", StringComparison.Ordinal)
                || CurrentTxMessage.EndsWith(" 73", StringComparison.Ordinal))
            {
                // We already sent closing; their ack finishes the QSO.
                FinishContact();
                return CanLog();
            }

            CurrentTxMessage = Ft4MessageCodec.Build73(TheirCall, my);
            return false;
        }

        if (Ft4MessageCodec.IsGrid(extra))
        {
            TheirGrid = extra;
            // We answered their CQ and still owe our grid: do not jump to a report.
            if (IsPendingGridReply(my))
                return false;

            ReportSent = Ft4MessageCodec.FormatSnrReport(decode.SnrDb);
            CurrentTxMessage = Ft4MessageCodec.BuildReport(TheirCall, my, ReportSent);
            return false;
        }

        if (Ft4MessageCodec.IsReport(extra))
        {
            ReplyToReport(TheirCall, my, extra, decode.SnrDb);
            return false;
        }

        return false;
    }

    /// <summary>
    /// Plain +NN gets R+NN back. An R+NN, or a plain report after we already sent our own
    /// plain report, advances to RR73 or RRR. A repeated +NN while R+NN is already queued
    /// stays on R+NN.
    /// </summary>
    private void ReplyToReport(string? theirCall, string my, string? extra, float snrDb)
    {
        if (string.IsNullOrWhiteSpace(theirCall))
            return;

        ReportReceived = Ft4MessageCodec.NormalizeSnrReport(extra);
        var plain = !Ft4MessageCodec.IsRogerReport(extra);
        if (plain && (ReportSent is null || IsOutgoingRogerReport(CurrentTxMessage)))
        {
            ReportSent = Ft4MessageCodec.FormatSnrReport(snrDb);
            CurrentTxMessage = Ft4MessageCodec.BuildReport(
                theirCall,
                my,
                Ft4MessageCodec.FormatRogerReport(snrDb));
            return;
        }

        ReportSent ??= Ft4MessageCodec.FormatSnrReport(snrDb);
        CurrentTxMessage = _skipRrr()
            ? Ft4MessageCodec.BuildRr73(theirCall, my)
            : Ft4MessageCodec.BuildRrr(theirCall, my);
    }

    private static bool IsOutgoingRogerReport(string message) =>
        Ft4MessageCodec.TryParse(message, out _, out _, out var extra)
        && Ft4MessageCodec.IsRogerReport(extra);

    /// <summary>
    /// After RR73, TX stops. A repeated report means they did not copy it, so send RR73
    /// (or RRR) again. Their 73 leaves the contact finished.
    /// </summary>
    private void ResumeClosingIfTheyRepeatReport(string? callTo, string callDe, string? extra, string my)
    {
        if (TheirCall is null || string.IsNullOrWhiteSpace(my))
            return;
        if (!callDe.Equals(TheirCall, StringComparison.OrdinalIgnoreCase))
            return;
        if (!Ft4MessageCodec.IsAddressedTo(callTo, my))
            return;
        if (!Ft4MessageCodec.IsReport(extra))
            return;

        ReportReceived ??= Ft4MessageCodec.NormalizeSnrReport(extra);
        CurrentTxMessage = _skipRrr()
            ? Ft4MessageCodec.BuildRr73(TheirCall, my)
            : Ft4MessageCodec.BuildRrr(TheirCall, my);
        Phase = Ft4QsoPhase.InQso;
        TransmitEnabled = true;
        QsoCompletedUtc = null;
    }

    /// <summary>True while our next TX is still the first grid reply to their CQ.</summary>
    private bool IsPendingGridReply(string my)
    {
        if (TheirCall is null || ReportSent is not null)
            return false;

        return CurrentTxMessage.Equals(
            Ft4MessageCodec.BuildGridReply(TheirCall, my, _myGrid()),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Operator asked to send a signal report again. Reuses the report already sent,
    /// or <paramref name="snrDb"/> when none has been sent yet.
    /// </summary>
    public bool ForceReport(float? snrDb)
    {
        lock (_gate)
            return ForceReportCore(snrDb);
    }

    private bool ForceReportCore(float? snrDb)
    {
        if (string.IsNullOrWhiteSpace(TheirCall))
            return false;

        var my = _myCall();
        if (string.IsNullOrWhiteSpace(my))
            return false;

        var report = ReportSent ?? Ft4MessageCodec.FormatSnrReport(snrDb ?? 0f);
        ReportSent = report;
        CurrentTxMessage = Ft4MessageCodec.BuildReport(TheirCall, my, report);
        Phase = Ft4QsoPhase.InQso;
        TransmitEnabled = true;
        return true;
    }

    /// <summary>Operator asked to send 73 again to the station in the current QSO.</summary>
    public bool Force73()
    {
        lock (_gate)
            return Force73Core();
    }

    private bool Force73Core()
    {
        if (string.IsNullOrWhiteSpace(TheirCall))
            return false;

        var my = _myCall();
        if (string.IsNullOrWhiteSpace(my))
            return false;

        CurrentTxMessage = Ft4MessageCodec.Build73(TheirCall, my);
        Phase = Ft4QsoPhase.InQso;
        TransmitEnabled = true;
        return true;
    }

    /// <summary>Called after a TX message was fully sent.</summary>
    public bool OnTxCompleted()
    {
        lock (_gate)
        {
            if (Phase != Ft4QsoPhase.InQso)
                return false;

            var msg = CurrentTxMessage;
            if (_skipRrr() && msg.EndsWith(" RR73", StringComparison.Ordinal))
            {
                FinishContact();
                return CanLog();
            }

            if (msg.EndsWith(" 73", StringComparison.Ordinal)
                && !msg.EndsWith(" RR73", StringComparison.Ordinal))
            {
                FinishContact();
                return CanLog();
            }

            return false;
        }
    }

    private void FinishContact()
    {
        TransmitEnabled = false;
        Phase = Ft4QsoPhase.Finished;
        QsoCompletedUtc = DateTime.UtcNow;
    }

    public bool CanLog() =>
        TheirCall is not null
        && ReportSent is not null
        && ReportReceived is not null;
}
