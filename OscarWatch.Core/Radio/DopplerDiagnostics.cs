using OscarWatch.Core.Models;
using OscarWatch.Core.Orbit;

namespace OscarWatch.Core.Radio;

public static class DopplerDiagnostics
{
    public static DopplerPassLogEntry Capture(
        IOrbitPropagator? propagator,
        RigSettings settings,
        GroundStation site,
        RigTrackingContext context,
        DateTime utc,
        int baseThresholdHz,
        int effectiveThresholdHz,
        CorrectedFrequencies corrected,
        long lastRigRxHz,
        long lastRigTxHz,
        double passbandDlKHz,
        double passbandUlKHz,
        string eventName,
        bool wroteRx = false,
        bool wroteTx = false,
        bool belowThreshold = false,
        bool interactive = false,
        string dialTracking = DopplerDialTrackingMode.Automatic,
        long mainDialHz = 0,
        long dialVsCatHz = 0,
        bool vfoStable = false,
        bool rigTracking = true,
        bool catPaused = false,
        string? skipReason = null,
        string? notes = null)
    {
        var look = context.TrackState.LookAngles;
        var rangeRate = look?.RangeRateKmPerSec ?? 0;
        var slope = 0.0;
        var slew = 0.0;
        var lead = new DopplerLeadRangeRates(rangeRate, rangeRate, 0);

        if (propagator is not null && look is not null)
        {
            slope = DopplerCatLead.ComputeRangeRateSlopeKmPerSec2(
                propagator,
                context.TrackState.NoradId,
                site,
                utc,
                rangeRate);
            slew = DopplerAdaptiveThreshold.EstimateMaxSlewHzPerSec(
                propagator,
                context.TrackState.NoradId,
                site,
                utc,
                rangeRate,
                context.Mode.DownlinkKHz,
                context.Mode.UplinkKHz,
                context.DopplerStrategy,
                context.Mode.IsBeaconOnly);
            lead = DopplerCatLead.ResolveRangeRates(
                propagator,
                settings,
                site,
                context.TrackState,
                utc,
                context.Mode.DownlinkMode,
                context.Mode.UplinkMode);
        }

        var rxHz = (long)Math.Round(corrected.RadioReceiveKHz * 1000.0);
        var txHz = (long)Math.Round(corrected.RadioTransmitKHz * 1000.0);

        return new DopplerPassLogEntry(
            Utc: utc,
            Event: eventName,
            NoradId: context.TrackState.NoradId,
            SatelliteName: context.TrackState.Name,
            ElevationDeg: look?.ElevationDeg ?? double.NaN,
            AzimuthDeg: look?.AzimuthDeg ?? double.NaN,
            RangeRateKmPerSec: rangeRate,
            SlopeKmPerSec2: slope,
            SlewHzPerSec: slew,
            BaseThresholdHz: baseThresholdHz,
            EffectiveThresholdHz: effectiveThresholdHz,
            LeadEnabled: settings.DopplerCatLeadEnabled,
            LeadBlend: lead.LeadBlend,
            LeadGainPercent: settings.DopplerCatLeadGainPercent is > 0 and <= 100
                ? settings.DopplerCatLeadGainPercent
                : RigSettings.DefaultDopplerCatLeadGainPercent,
            LeadMsRx: DopplerCatLead.ResolveLeadMs(settings.ReceiveCatDelayMs(), rangeRate, settings.DopplerCatLeadMs),
            LeadMsTx: DopplerCatLead.ResolveLeadMs(settings.TransmitCatDelayMs(), rangeRate, settings.DopplerCatLeadMs),
            LeadRxRangeRate: lead.RxRangeRateKmPerSec,
            LeadTxRangeRate: lead.TxRangeRateKmPerSec,
            SatRxKHz: corrected.SatelliteReceiveKHz,
            SatTxKHz: corrected.SatelliteTransmitKHz,
            RadioRxKHz: corrected.RadioReceiveKHz,
            RadioTxKHz: corrected.RadioTransmitKHz,
            LastRigRxHz: lastRigRxHz,
            LastRigTxHz: lastRigTxHz,
            RxDeltaHz: Math.Abs(rxHz - lastRigRxHz),
            TxDeltaHz: Math.Abs(txHz - lastRigTxHz),
            RxOffsetKHz: context.ReceiveOffsetKHz,
            TxOffsetKHz: context.TransmitOffsetKHz,
            PassbandDlKHz: passbandDlKHz,
            PassbandUlKHz: passbandUlKHz,
            WroteRx: wroteRx,
            WroteTx: wroteTx,
            BelowThreshold: belowThreshold,
            Interactive: interactive,
            DialTracking: dialTracking,
            MainDialHz: mainDialHz,
            DialVsCatHz: dialVsCatHz,
            VfoStable: vfoStable,
            RigTracking: rigTracking,
            CatPaused: catPaused,
            SkipReason: skipReason,
            Notes: notes);
    }
}
