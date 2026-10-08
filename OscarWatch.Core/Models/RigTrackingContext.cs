using OscarWatch.Core.Radio;

namespace OscarWatch.Core.Models;

public sealed class RigTrackingContext
{
    public required SatelliteTrackState TrackState { get; init; }
    public required SatelliteTransponderMode Mode { get; init; }
    public required CorrectedFrequencies Corrected { get; init; }
    public double TransmitOffsetKHz { get; init; }
    public double ReceiveOffsetKHz { get; init; }
    public double PassbandDownlinkAdjustKHz { get; init; }
    public double PassbandUplinkAdjustKHz { get; init; }
    public double? SelectedCtcssHz { get; init; }

    /// <summary>When true, uplink mode is CW for linear SSB voice database entries.</summary>
    public bool CwUplink { get; init; }

    public bool CwKeepSidebandDownlink { get; init; }

    public DopplerStrategy DopplerStrategy { get; init; } = DopplerStrategy.Full;

    public string EffectiveUplinkMode =>
        TransponderOperatingModes.GetEffectiveUplinkMode(Mode, CwUplink);

    public string EffectiveDownlinkMode =>
        TransponderOperatingModes.GetEffectiveDownlinkMode(Mode, CwUplink, CwKeepSidebandDownlink);
}
