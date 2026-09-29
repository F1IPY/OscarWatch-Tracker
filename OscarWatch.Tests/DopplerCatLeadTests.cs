using OscarWatch.Core.Models;
using OscarWatch.Core.Orbit;
using OscarWatch.Core.Radio;
using OscarWatch.Orbit;
namespace OscarWatch.Tests;

public class DopplerCatLeadTests
{
    private static readonly GroundStation Site = new()
    {
        LatitudeDeg = 51.5,
        LongitudeDeg = -0.1,
        AltitudeMetersAsl = 50
    };

    private static SatelliteTrackState StateWithRate(double rangeRateKmPerSec) => new()
    {
        Name = "TEST",
        NoradId = "99999",
        Subpoint = new GeoCoordinate(0, 0, 400),
        LookAngles = new LookAngles(180, 30, 800, rangeRateKmPerSec)
    };

    [Fact]
    public void Disabled_returns_snapshot_range_rate()
    {
        var utc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var propagator = new StubPropagator(utc, snapshotRate: -3.5, slopeSampleRate: -2.0, rxLeadRate: 1.0, txLeadRate: 2.0);
        var settings = new RigSettings { DopplerCatLeadEnabled = false, CatDelayMs = 100 };

        var (rx, tx) = DopplerCatLead.ResolveRangeRates(
            propagator,
            settings,
            Site,
            StateWithRate(-3.5),
            DateTime.UtcNow);

        Assert.Equal(-3.5, rx);
        Assert.Equal(-3.5, tx);
        Assert.Equal(0, propagator.CallCount);
    }

    [Theory]
    [InlineData("DATA-USB", "DATA-LSB")]
    [InlineData("DATA-FM", "DATA-FM")]
    [InlineData("FM-DATA", "FM")]
    [InlineData("USB", "DATA-LSB")]
    public void Data_modes_ignore_lead_and_return_snapshot(string downlinkMode, string uplinkMode)
    {
        var utc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var propagator = new StubPropagator(utc, snapshotRate: -3.5, slopeSampleRate: -2.0, rxLeadRate: 1.0, txLeadRate: 2.0);
        var settings = new RigSettings { DopplerCatLeadEnabled = true, CatDelayMs = 100, DopplerCatLeadMs = 40 };

        var result = DopplerCatLead.ResolveRangeRates(
            propagator,
            settings,
            Site,
            StateWithRate(-3.5),
            utc,
            downlinkMode,
            uplinkMode);

        Assert.Equal(-3.5, result.RxRangeRateKmPerSec);
        Assert.Equal(-3.5, result.TxRangeRateKmPerSec);
        Assert.Equal(0, result.LeadBlend);
        Assert.Equal(0, propagator.CallCount);
    }

    [Fact]
    public void Explicit_lead_ms_works_when_cat_delay_is_zero()
    {
        var utc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var propagator = new StubPropagator(utc, snapshotRate: -3.5, slopeSampleRate: -2.0, rxLeadRate: 1.0, txLeadRate: 2.0);
        var settings = new RigSettings
        {
            DopplerCatLeadEnabled = true,
            CatDelayMs = 0,
            DopplerCatLeadMs = 40,
            DopplerCatLeadGainPercent = 100
        };

        var result = DopplerCatLead.ResolveRangeRates(propagator, settings, Site, StateWithRate(-3.5), utc);

        Assert.Equal(2, propagator.CallCount);
        Assert.True(result.LeadBlend > 0);
        Assert.Equal(1.0, result.RxRangeRateKmPerSec);
        Assert.Equal(1.0, result.TxRangeRateKmPerSec);
    }

    [Fact]
    public void Enabled_queries_propagator_at_half_cat_delay_per_leg_on_steep_slope()
    {
        var utc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var propagator = new StubPropagator(utc, snapshotRate: -3.5, slopeSampleRate: -2.0, rxLeadRate: 1.0, txLeadRate: 2.0);
        var settings = new RigSettings
        {
            DopplerCatLeadEnabled = true,
            CatDelayMs = 100,
            DopplerCatLeadMs = 0,
            DopplerCatLeadGainPercent = 100
        };
        var state = StateWithRate(-3.5);

        var (rx, tx) = DopplerCatLead.ResolveRangeRates(propagator, settings, Site, state, utc);

        // With equal rx/tx lead (both 50 ms), the short-circuit calls propagator once for both
        Assert.Equal(2, propagator.CallCount); // 1 slope + 1 shared lead
        Assert.Equal(utc.AddSeconds(1), propagator.LastSlopeUtc);
        Assert.Equal(utc.AddMilliseconds(50), propagator.LastRxUtc);
        Assert.Equal(1.0, rx);
        Assert.Equal(1.0, tx); // same rate used for both when leads are equal
    }

    [Fact]
    public void Mid_slope_blends_between_snapshot_and_lead_rates()
    {
        var utc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var propagator = new StubPropagator(utc, snapshotRate: -3.0, slopeSampleRate: -2.987, rxLeadRate: 1.0, txLeadRate: 2.0);
        var settings = new RigSettings { DopplerCatLeadEnabled = true, CatDelayMs = 100, DopplerCatLeadMs = 0, DopplerCatLeadGainPercent = 100 };
        var state = StateWithRate(-3.0);

        var result = DopplerCatLead.ResolveRangeRates(propagator, settings, Site, state, utc);

        Assert.InRange(result.LeadBlend, 0.4, 0.6);
        Assert.InRange(result.RxRangeRateKmPerSec, -1.01, -0.99);
        // With equal leads (both 50 ms), tx gets the same blended rate as rx
        Assert.InRange(result.TxRangeRateKmPerSec, -1.01, -0.99);
        Assert.NotEqual(-3.0, result.RxRangeRateKmPerSec);
        Assert.NotEqual(1.0, result.RxRangeRateKmPerSec);
    }

    [Fact]
    public void Gentle_slope_returns_snapshot_without_lead_queries()
    {
        var utc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var propagator = new StubPropagator(utc, snapshotRate: -3.5, slopeSampleRate: -3.5, rxLeadRate: 1.0, txLeadRate: 2.0);
        var settings = new RigSettings { DopplerCatLeadEnabled = true, CatDelayMs = 100, DopplerCatLeadMs = 0, DopplerCatLeadGainPercent = 100 };
        var state = StateWithRate(-3.5);

        var (rx, tx) = DopplerCatLead.ResolveRangeRates(propagator, settings, Site, state, utc);

        Assert.Equal(1, propagator.CallCount);
        Assert.Equal(utc.AddSeconds(1), propagator.LastSlopeUtc);
        Assert.Equal(-3.5, rx);
        Assert.Equal(-3.5, tx);
    }

    [Fact]
    public void Dual_radio_uses_per_leg_cat_delay()
    {
        var utc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var settings = new RigSettings
        {
            DopplerCatLeadEnabled = true,
            DualRadioEnabled = true,
            DopplerCatLeadMs = 0,
            Downlink = new RigEndpointSettings { CatDelayMs = 80 },
            Uplink = new RigEndpointSettings { CatDelayMs = 120 }
        };

        var steep = new StubPropagator(utc, snapshotRate: 1.5, slopeSampleRate: 0.2, rxLeadRate: 1.0, txLeadRate: 2.0);
        DopplerCatLead.ResolveRangeRates(steep, settings, Site, StateWithRate(1.5), utc);

        Assert.Equal(utc.AddMilliseconds(40), steep.LastRxUtc);
        Assert.Equal(utc.AddMilliseconds(50), steep.LastTxUtc);
    }

    [Fact]
    public void Propagation_failure_falls_back_to_snapshot_rate()
    {
        var propagator = new ThrowingPropagator();
        var settings = new RigSettings { DopplerCatLeadEnabled = true, CatDelayMs = 100, DopplerCatLeadMs = 0, DopplerCatLeadGainPercent = 100 };

        var (rx, tx) = DopplerCatLead.ResolveRangeRates(
            propagator,
            settings,
            Site,
            StateWithRate(-2.2),
            DateTime.UtcNow);

        Assert.Equal(-2.2, rx);
        Assert.Equal(-2.2, tx);
    }

    [Fact]
    public void Lead_produces_different_doppler_than_now_on_fo29_high_pass_segment()
    {
        var entry = new SatelliteCatalogEntry
        {
            Name = "FO-29",
            NoradId = "24278",
            Line1 = "1 24278U 96046B   26141.17662052  .00000000  00000-0  34829-4 0  9991",
            Line2 = "2 24278  98.5266 353.7450 0350115 166.3802 194.7089 13.53272915469510"
        };
        var propagator = new PublicOrbitToolsPropagator();
        propagator.LoadSatellite(entry);

        var site = new GroundStation { LatitudeDeg = 51.5, LongitudeDeg = -0.1, AltitudeMetersAsl = 50 };
        var (tca, maxEl) = FindHighElevationPassTcaUtc(propagator, entry.NoradId, site, minElevationDeg: 60);
        var utc = FindSteepestUtcNearTca(propagator, entry.NoradId, site, tca);
        var state = new SatelliteTrackState
        {
            Name = entry.Name,
            NoradId = entry.NoradId,
            Subpoint = propagator.GetSubpoint(entry.NoradId, utc),
            LookAngles = propagator.GetLookAngles(entry.NoradId, site, utc)
        };

        var mode = new SatelliteTransponderMode
        {
            DownlinkKHz = 435_850.45,
            UplinkKHz = 145_952.65,
            DownlinkMode = "USB",
            UplinkMode = "LSB",
            Doppler = "REV"
        };

        var nowRate = state.LookAngles!.RangeRateKmPerSec;
        var nowCorrected = DopplerFrequencyCalculator.Compute(mode, nowRate, 0);

        var settings = new RigSettings { DopplerCatLeadEnabled = true, CatDelayMs = 100, DopplerCatLeadMs = 0, DopplerCatLeadGainPercent = 100 };
        var (rxLead, _) = DopplerCatLead.ResolveRangeRates(propagator, settings, site, state, utc);
        var leadCorrected = DopplerFrequencyCalculator.Compute(mode, rxLead, 0);

        Assert.True(maxEl >= 60, $"Expected a high FO-29 pass over the test site (best el {maxEl:F1}°).");
        Assert.NotEqual(nowRate, rxLead);
        Assert.NotEqual(nowCorrected.RadioReceiveKHz, leadCorrected.RadioReceiveKHz);
    }

    [Fact]
    public void Fo29_lead_applies_near_tca_not_on_gentle_aos_leg()
    {
        var entry = new SatelliteCatalogEntry
        {
            Name = "FO-29",
            NoradId = "24278",
            Line1 = "1 24278U 96046B   26141.17662052  .00000000  00000-0  34829-4 0  9991",
            Line2 = "2 24278  98.5266 353.7450 0350115 166.3802 194.7089 13.53272915469510"
        };
        var propagator = new PublicOrbitToolsPropagator();
        propagator.LoadSatellite(entry);

        var site = new GroundStation { LatitudeDeg = 51.5, LongitudeDeg = -0.1, AltitudeMetersAsl = 50 };
        var (tca, maxEl) = FindHighElevationPassTcaUtc(propagator, entry.NoradId, site, minElevationDeg: 60);
        var settings = new RigSettings { DopplerCatLeadEnabled = true, CatDelayMs = 100, DopplerCatLeadMs = 0, DopplerCatLeadGainPercent = 100 };

        static SatelliteTrackState StateAt(
            PublicOrbitToolsPropagator propagator,
            SatelliteCatalogEntry entry,
            GroundStation site,
            DateTime utc) => new()
        {
            Name = entry.Name,
            NoradId = entry.NoradId,
            Subpoint = propagator.GetSubpoint(entry.NoradId, utc),
            LookAngles = propagator.GetLookAngles(entry.NoradId, site, utc)
        };

        var aosUtc = tca.AddMinutes(-4);
        var steepUtc = FindSteepestUtcNearTca(propagator, entry.NoradId, site, tca);
        var aosState = StateAt(propagator, entry, site, aosUtc);
        var steepState = StateAt(propagator, entry, site, steepUtc);

        var aosSlope = DopplerCatLead.ComputeRangeRateSlopeKmPerSec2(
            propagator, entry.NoradId, site, aosUtc, aosState.LookAngles!.RangeRateKmPerSec);
        var steepSlope = DopplerCatLead.ComputeRangeRateSlopeKmPerSec2(
            propagator, entry.NoradId, site, steepUtc, steepState.LookAngles!.RangeRateKmPerSec);

        var (aosRx, _) = DopplerCatLead.ResolveRangeRates(propagator, settings, site, aosState, aosUtc);
        var (steepRx, _) = DopplerCatLead.ResolveRangeRates(propagator, settings, site, steepState, steepUtc);

        Assert.True(maxEl >= 60);
        Assert.True(aosSlope < DopplerCatLead.SlopeBlendStartKmPerSec2,
            $"AOS leg slope {aosSlope:F4} km/s² should stay below blend start.");
        Assert.True(steepSlope >= DopplerCatLead.SteepRangeRateSlopeKmPerSec2,
            $"Steep leg slope {steepSlope:F4} km/s² should exceed steep threshold.");
        Assert.Equal(aosState.LookAngles!.RangeRateKmPerSec, aosRx);
        Assert.NotEqual(steepState.LookAngles!.RangeRateKmPerSec, steepRx);
    }

    [Fact]
    public void Fo29_high_pass_lead_overshoot_is_bounded_after_cap()
    {
        var entry = new SatelliteCatalogEntry
        {
            Name = "FO-29",
            NoradId = "24278",
            Line1 = "1 24278U 96046B   26141.17662052  .00000000  00000-0  34829-4 0  9991",
            Line2 = "2 24278  98.5266 353.7450 0350115 166.3802 194.7089 13.53272915469510"
        };
        var propagator = new PublicOrbitToolsPropagator();
        propagator.LoadSatellite(entry);

        var site = new GroundStation { LatitudeDeg = 51.5, LongitudeDeg = -0.1, AltitudeMetersAsl = 50 };
        var (tca, maxEl) = FindHighElevationPassTcaUtc(propagator, entry.NoradId, site, minElevationDeg: 60);

        var mode = new SatelliteTransponderMode
        {
            DownlinkKHz = 435_850.45,
            UplinkKHz = 145_952.65,
            DownlinkMode = "USB",
            UplinkMode = "LSB",
            Doppler = "REV"
        };

        const int catDelayMs = 200;
        var cappedLeadMs = DopplerCatLead.ResolveLeadMs(catDelayMs);
        var uncappedLeadMs = catDelayMs / 2.0;

        double MaxOvershootHz(double leadMs)
        {
            var maxHz = 0.0;
            var start = tca.AddMinutes(-3);
            var end = tca.AddMinutes(5);
            for (var t = start; t <= end; t = t.AddSeconds(15))
            {
                var look = propagator.GetLookAngles(entry.NoradId, site, t);
                if (look.ElevationDeg < 5)
                    continue;

                var nowHz = DopplerFrequencyCalculator.Compute(mode, look.RangeRateKmPerSec, 0).RadioReceiveKHz * 1000.0;
                var leadRate = propagator.GetLookAngles(entry.NoradId, site, t.AddMilliseconds(leadMs)).RangeRateKmPerSec;
                var leadHz = DopplerFrequencyCalculator.Compute(mode, leadRate, 0).RadioReceiveKHz * 1000.0;
                maxHz = Math.Max(maxHz, Math.Abs(leadHz - nowHz));
            }

            return maxHz;
        }

        var uncappedHz = MaxOvershootHz(uncappedLeadMs);
        var cappedHz = MaxOvershootHz(cappedLeadMs);

        double MaxOvershootHzFine(double leadMs)
        {
            var maxHz = 0.0;
            for (var t = tca.AddSeconds(-90); t <= tca.AddSeconds(180); t = t.AddSeconds(1))
            {
                var look = propagator.GetLookAngles(entry.NoradId, site, t);
                if (look.ElevationDeg < 10)
                    continue;

                var nowHz = DopplerFrequencyCalculator.Compute(mode, look.RangeRateKmPerSec, 0).RadioReceiveKHz * 1000.0;
                var leadRate = propagator.GetLookAngles(entry.NoradId, site, t.AddMilliseconds(leadMs)).RangeRateKmPerSec;
                var leadHz = DopplerFrequencyCalculator.Compute(mode, leadRate, 0).RadioReceiveKHz * 1000.0;
                maxHz = Math.Max(maxHz, Math.Abs(leadHz - nowHz));
            }

            return maxHz;
        }

        var fineUncappedHz = MaxOvershootHzFine(uncappedLeadMs);
        var fineCappedHz = MaxOvershootHzFine(cappedLeadMs);

        // FO-29 high-el pass, CAT delay 200 ms: peak RX |lead−now| is ~6 Hz uncapped / ~3 Hz capped (1 s steps, TCA±3 min).
        Assert.True(maxEl >= 60);
        Assert.True(fineCappedHz < fineUncappedHz);
        Assert.InRange(fineUncappedHz, 2, 12);
        Assert.InRange(fineCappedHz, 1, 6);
    }

    [Fact]
    public void Jo97_high_pass_lead_overshoot_is_small_on_vhf_downlink()
    {
        var entry = new SatelliteCatalogEntry
        {
            Name = "JO-97",
            NoradId = "43803",
            Line1 = "1 43803U 18099AX  26141.14090581  .00011070  00000-0  35283-3 0  9990",
            Line2 = "2 43803  97.4139 203.8986 0005533  24.8679 335.2828 15.32693080409484"
        };
        var propagator = new PublicOrbitToolsPropagator();
        propagator.LoadSatellite(entry);

        var site = new GroundStation { LatitudeDeg = 51.5, LongitudeDeg = -0.1, AltitudeMetersAsl = 50 };
        var (tca, maxEl) = FindHighElevationPassTcaUtc(propagator, entry.NoradId, site, minElevationDeg: 40);

        var mode = new SatelliteTransponderMode
        {
            DownlinkKHz = 145_865,
            UplinkKHz = 435_110.1,
            DownlinkMode = "USB",
            UplinkMode = "LSB",
            Doppler = "REV"
        };

        var settings = new RigSettings { DopplerCatLeadEnabled = true, CatDelayMs = 100, DopplerCatLeadMs = 0, DopplerCatLeadGainPercent = 100 };
        var cappedLeadMs = DopplerCatLead.ResolveLeadMs(settings.CatDelayMs);

        double MaxGateStepHz()
        {
            var maxHz = 0.0;
            for (var t = tca.AddSeconds(-120); t <= tca.AddSeconds(180); t = t.AddSeconds(1))
            {
                var look = propagator.GetLookAngles(entry.NoradId, site, t);
                if (look.ElevationDeg < 10)
                    continue;

                var state = new SatelliteTrackState
                {
                    Name = entry.Name,
                    NoradId = entry.NoradId,
                    Subpoint = propagator.GetSubpoint(entry.NoradId, t),
                    LookAngles = look
                };

                var nowHz = DopplerFrequencyCalculator.Compute(mode, look.RangeRateKmPerSec, 0).RadioReceiveKHz * 1000.0;
                var (leadRx, _) = DopplerCatLead.ResolveRangeRates(propagator, settings, site, state, t);
                var leadHz = DopplerFrequencyCalculator.Compute(mode, leadRx, 0).RadioReceiveKHz * 1000.0;
                maxHz = Math.Max(maxHz, Math.Abs(leadHz - nowHz));
            }

            return maxHz;
        }

        var gateStepHz = MaxGateStepHz();

        Assert.True(maxEl >= 40);
        // JO-97 VHF downlink: peak |lead−now| on RX is a few Hz, not hundreds.
        Assert.InRange(gateStepHz, 0, 10);
        Assert.Equal(50, cappedLeadMs);
    }

    [Fact]
    public void Receding_high_range_rate_uses_extended_lead_ms_cap()
    {
        Assert.Equal(50, DopplerCatLead.ResolveLeadMs(100, -3.0));
        Assert.Equal(75, DopplerCatLead.ResolveLeadMs(100, 3.0));
        Assert.Equal(75, DopplerCatLead.ResolveLeadMs(200, 4.5));
        Assert.Equal(50, DopplerCatLead.ResolveLeadMs(200, 0));
    }

    [Fact]
    public void High_cat_delay_lead_is_capped_at_max_ms()
    {
        Assert.Equal(25, DopplerCatLead.ResolveLeadMs(50));
        Assert.Equal(50, DopplerCatLead.ResolveLeadMs(100));
        Assert.Equal(50, DopplerCatLead.ResolveLeadMs(200));
        Assert.Equal(50, DopplerCatLead.ResolveLeadMs(500));
    }

    [Fact]
    public void Capped_lead_queries_propagator_at_max_not_half_delay()
    {
        var utc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var propagator = new StubPropagator(utc, snapshotRate: 1.0, slopeSampleRate: 0.2, rxLeadRate: 1.0, txLeadRate: 2.0);
        var settings = new RigSettings
        {
            DopplerCatLeadEnabled = true,
            CatDelayMs = 200,
            DopplerCatLeadMs = 0
        };

        DopplerCatLead.ResolveRangeRates(propagator, settings, Site, StateWithRate(1.0), utc);

        // With equal leads (both capped to 50 ms), short-circuit calls propagator once
        Assert.Equal(utc.AddMilliseconds(50), propagator.LastRxUtc);
    }

    [Fact]
    public void Rs44_post_tca_43deg_residual_leg_activates_cat_lead()
    {
        var entry = new SatelliteCatalogEntry
        {
            Name = "RS-44",
            NoradId = "44909",
            Line1 = "1 44909U 19096E   26141.11069286  .00000018  00000-0  30335-4 0  9995",
            Line2 = "2 44909  82.5230 357.7010 0216952 207.4466 151.5042 12.79748393298881"
        };
        var propagator = new PublicOrbitToolsPropagator();
        propagator.LoadSatellite(entry);

        var site = new GroundStation { LatitudeDeg = 51.5, LongitudeDeg = -0.1, AltitudeMetersAsl = 50 };
        var (tca, maxEl) = FindHighElevationPassTcaUtc(propagator, entry.NoradId, site, minElevationDeg: 60);
        var settings = new RigSettings { DopplerCatLeadEnabled = true, CatDelayMs = 100, DopplerCatLeadMs = 0, DopplerCatLeadGainPercent = 100 };

        DateTime? postTcaAt43Utc = null;
        double postTcaBlend = 0;
        double postTcaSlope = 0;
        var maxPostTcaDopplerHzPerSec = 0.0;

        var mode = new SatelliteTransponderMode
        {
            DownlinkKHz = 435_667,
            UplinkKHz = 145_937.61,
            DownlinkMode = "USB",
            UplinkMode = "LSB",
            Doppler = "REV"
        };

        for (var offset = 1; offset <= 900; offset += 1)
        {
            var t = tca.AddSeconds(offset);
            var look = propagator.GetLookAngles(entry.NoradId, site, t);
            if (look.ElevationDeg < 10)
                break;

            var next = propagator.GetLookAngles(entry.NoradId, site, t.AddSeconds(1));
            var nowHz = DopplerFrequencyCalculator.Compute(mode, look.RangeRateKmPerSec, 0).RadioReceiveKHz * 1000.0;
            var nextHz = DopplerFrequencyCalculator.Compute(mode, next.RangeRateKmPerSec, 0).RadioReceiveKHz * 1000.0;
            maxPostTcaDopplerHzPerSec = Math.Max(maxPostTcaDopplerHzPerSec, Math.Abs(nextHz - nowHz));

            if (look.ElevationDeg is >= 42 and <= 44)
            {
                postTcaAt43Utc ??= t;
                var state = new SatelliteTrackState
                {
                    Name = entry.Name,
                    NoradId = entry.NoradId,
                    Subpoint = propagator.GetSubpoint(entry.NoradId, t),
                    LookAngles = look
                };
                var lead = DopplerCatLead.ResolveRangeRates(propagator, settings, site, state, t);
                postTcaSlope = DopplerCatLead.ComputeRangeRateSlopeKmPerSec2(
                    propagator, entry.NoradId, site, t, look.RangeRateKmPerSec);
                postTcaBlend = Math.Max(postTcaBlend, lead.LeadBlend);
            }
        }

        Assert.True(maxEl >= 60, $"Expected a high RS-44 pass (best el {maxEl:F1}°).");
        Assert.NotNull(postTcaAt43Utc);
        Assert.True(maxPostTcaDopplerHzPerSec > 30,
            $"Post-TCA Doppler slew {maxPostTcaDopplerHzPerSec:F1} Hz/s exceeds threshold stepping.");
        Assert.InRange(postTcaSlope, DopplerCatLead.ResidualAssistSlopeStartKmPerSec2, DopplerCatLead.SteepRangeRateSlopeKmPerSec2);
        Assert.True(postTcaBlend >= 0.08,
            $"Post-TCA 43° blend {postTcaBlend:F3}, slope {postTcaSlope:F4} km/s².");
    }

    [Fact]
    public void Fo29_post_tca_low_slope_receding_leg_keeps_cat_lead()
    {
        var entry = new SatelliteCatalogEntry
        {
            Name = "FO-29",
            NoradId = "24278",
            Line1 = "1 24278U 96046B   26141.17662052  .00000000  00000-0  34829-4 0  9991",
            Line2 = "2 24278  98.5266 353.7450 0350115 166.3802 194.7089 13.53272915469510"
        };
        var propagator = new PublicOrbitToolsPropagator();
        propagator.LoadSatellite(entry);

        var site = new GroundStation { LatitudeDeg = 51.5, LongitudeDeg = -0.1, AltitudeMetersAsl = 50 };
        var (tca, maxEl) = FindHighElevationPassTcaUtc(propagator, entry.NoradId, site, minElevationDeg: 60);
        var settings = new RigSettings { DopplerCatLeadEnabled = true, CatDelayMs = 100, DopplerCatLeadMs = 0, DopplerCatLeadGainPercent = 100 };

        // ~3.5 min after TCA: slope below blend start but large positive range rate (receding leg).
        var utc = tca.AddSeconds(210);
        var look = propagator.GetLookAngles(entry.NoradId, site, utc);
        var state = new SatelliteTrackState
        {
            Name = entry.Name,
            NoradId = entry.NoradId,
            Subpoint = propagator.GetSubpoint(entry.NoradId, utc),
            LookAngles = look
        };

        var slope = DopplerCatLead.ComputeRangeRateSlopeKmPerSec2(
            propagator, entry.NoradId, site, utc, look.RangeRateKmPerSec);
        var lead = DopplerCatLead.ResolveRangeRates(propagator, settings, site, state, utc);

        Assert.True(maxEl >= 60);
        Assert.True(look.RangeRateKmPerSec > 0, "Expected receding (positive) range rate post-TCA.");
        Assert.True(slope < DopplerCatLead.SlopeBlendStartKmPerSec2,
            $"Slope {slope:F4} km/s² should be below blend start on late post-TCA leg.");
        Assert.True(lead.LeadBlend >= 0.50,
            $"Post-TCA receding blend {lead.LeadBlend:F3} should stay active (slope {slope:F4}).");
        Assert.NotEqual(look.RangeRateKmPerSec, lead.RxRangeRateKmPerSec);
    }

    [Fact]
    public void Fo29_late_post_tca_los_leg_keeps_receding_floor_blend()
    {
        var entry = new SatelliteCatalogEntry
        {
            Name = "FO-29",
            NoradId = "24278",
            Line1 = "1 24278U 96046B   26141.17662052  .00000000  00000-0  34829-4 0  9991",
            Line2 = "2 24278  98.5266 353.7450 0350115 166.3802 194.7089 13.53272915469510"
        };
        var propagator = new PublicOrbitToolsPropagator();
        propagator.LoadSatellite(entry);

        var site = new GroundStation { LatitudeDeg = 51.5, LongitudeDeg = -0.1, AltitudeMetersAsl = 50 };
        var (tca, maxEl) = FindHighElevationPassTcaUtc(propagator, entry.NoradId, site, minElevationDeg: 60);
        var settings = new RigSettings { DopplerCatLeadEnabled = true, CatDelayMs = 100, DopplerCatLeadMs = 0, DopplerCatLeadGainPercent = 100 };

        var utc = tca.AddSeconds(300);
        var look = propagator.GetLookAngles(entry.NoradId, site, utc);
        var state = new SatelliteTrackState
        {
            Name = entry.Name,
            NoradId = entry.NoradId,
            Subpoint = propagator.GetSubpoint(entry.NoradId, utc),
            LookAngles = look
        };

        var slope = DopplerCatLead.ComputeRangeRateSlopeKmPerSec2(
            propagator, entry.NoradId, site, utc, look.RangeRateKmPerSec);
        var lead = DopplerCatLead.ResolveRangeRates(propagator, settings, site, state, utc);

        Assert.True(maxEl >= 60);
        Assert.True(look.RangeRateKmPerSec > 0);
        Assert.True(slope < DopplerCatLead.SlopeBlendStartKmPerSec2,
            $"Late LOS slope {slope:F4} km/s² should be below blend start.");
        Assert.True(lead.LeadBlend >= 0.35,
            $"Late post-TCA blend {lead.LeadBlend:F3} should stay active via receding floor.");
        Assert.Equal(75, DopplerCatLead.ResolveLeadMs(settings.CatDelayMs, look.RangeRateKmPerSec));
    }

    [Fact]
    public void Fo29_aos_approach_with_low_slope_stays_on_snapshot_rate()
    {
        var entry = new SatelliteCatalogEntry
        {
            Name = "FO-29",
            NoradId = "24278",
            Line1 = "1 24278U 96046B   26141.17662052  .00000000  00000-0  34829-4 0  9991",
            Line2 = "2 24278  98.5266 353.7450 0350115 166.3802 194.7089 13.53272915469510"
        };
        var propagator = new PublicOrbitToolsPropagator();
        propagator.LoadSatellite(entry);

        var site = new GroundStation { LatitudeDeg = 51.5, LongitudeDeg = -0.1, AltitudeMetersAsl = 50 };
        var (tca, maxEl) = FindHighElevationPassTcaUtc(propagator, entry.NoradId, site, minElevationDeg: 60);
        var settings = new RigSettings { DopplerCatLeadEnabled = true, CatDelayMs = 100, DopplerCatLeadMs = 0, DopplerCatLeadGainPercent = 100 };

        var utc = tca.AddSeconds(-240);
        var look = propagator.GetLookAngles(entry.NoradId, site, utc);
        var state = new SatelliteTrackState
        {
            Name = entry.Name,
            NoradId = entry.NoradId,
            Subpoint = propagator.GetSubpoint(entry.NoradId, utc),
            LookAngles = look
        };

        var slope = DopplerCatLead.ComputeRangeRateSlopeKmPerSec2(
            propagator, entry.NoradId, site, utc, look.RangeRateKmPerSec);
        var (rx, _) = DopplerCatLead.ResolveRangeRates(propagator, settings, site, state, utc);

        Assert.True(maxEl >= 60);
        Assert.True(look.RangeRateKmPerSec < 0, "Expected approaching (negative) range rate on AOS.");
        Assert.True(slope < DopplerCatLead.SlopeBlendStartKmPerSec2,
            $"AOS slope {slope:F4} km/s² should stay below blend start.");
        Assert.Equal(look.RangeRateKmPerSec, rx);
    }

    [Fact]
    public void Null_propagator_returns_snapshot_rate()
    {
        var settings = new RigSettings { DopplerCatLeadEnabled = true, CatDelayMs = 100, DopplerCatLeadMs = 0, DopplerCatLeadGainPercent = 100 };

        var (rx, tx) = DopplerCatLead.ResolveRangeRates(
            null,
            settings,
            Site,
            StateWithRate(4.1),
            DateTime.UtcNow);

        Assert.Equal(4.1, rx);
        Assert.Equal(4.1, tx);
    }

    [Fact]
    public void Tca_vicinity_tapers_lead_when_range_rate_is_small()
    {
        var raw = DopplerCatLead.ComputeLeadBlend(DopplerCatLead.SteepRangeRateSlopeKmPerSec2, 0.187);
        Assert.Equal(1.0, raw);

        Assert.Equal(0, DopplerCatLead.ApplyTcaRangeRateTaper(raw, 0));

        var tapered = DopplerCatLead.ApplyTcaRangeRateTaper(raw, 0.187);
        Assert.InRange(tapered, 0.45, 0.60);
    }

    [Fact]
    public void Rs44_at_tca_lead_blend_is_tapered()
    {
        var entry = new SatelliteCatalogEntry
        {
            Name = "RS-44",
            NoradId = "44909",
            Line1 = "1 44909U 19096E   26141.11069286  .00000018  00000-0  30335-4 0  9995",
            Line2 = "2 44909  82.5230 357.7010 0216952 207.4466 151.5042 12.79748393298881"
        };
        var propagator = new PublicOrbitToolsPropagator();
        propagator.LoadSatellite(entry);
        var site = new GroundStation { LatitudeDeg = 51.5, LongitudeDeg = -0.1, AltitudeMetersAsl = 50 };
        var (tca, maxEl) = FindHighElevationPassTcaUtc(propagator, entry.NoradId, site, minElevationDeg: 60);
        var settings = new RigSettings { DopplerCatLeadEnabled = true, CatDelayMs = 100, DopplerCatLeadMs = 0, DopplerCatLeadGainPercent = 100 };

        var look = propagator.GetLookAngles(entry.NoradId, site, tca);
        var state = new SatelliteTrackState
        {
            Name = entry.Name,
            NoradId = entry.NoradId,
            Subpoint = propagator.GetSubpoint(entry.NoradId, tca),
            LookAngles = look
        };
        var lead = DopplerCatLead.ResolveRangeRates(propagator, settings, site, state, tca);

        Assert.True(maxEl >= 60);
        Assert.True(Math.Abs(look.RangeRateKmPerSec) < 0.35, "TCA should have small |range rate|.");
        Assert.InRange(lead.LeadBlend, 0.05, 0.65);
        Assert.True(lead.LeadBlend < 0.85, "Near TCA, blend should be tapered below full strength.");
    }

    [Fact]
    public void User_lead_ms_overrides_automatic_cap()
    {
        Assert.Equal(35, DopplerCatLead.ResolveLeadMs(200, 1.0, 35));
        Assert.Equal(50, DopplerCatLead.ResolveLeadMs(200, 1.0, 0));
        Assert.Equal(75, DopplerCatLead.ResolveLeadMs(200, 4.5, 0));
        Assert.Equal(60, DopplerCatLead.ResolveLeadMs(200, 4.5, 60));
    }

    [Fact]
    public void Lead_gain_scales_lead_contribution_not_snapshot()
    {
        var utc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        const double snapshotRate = -3.5;
        const double leadRate = 1.0;
        var propagator = new StubPropagator(utc, snapshotRate: snapshotRate, slopeSampleRate: -2.0, rxLeadRate: leadRate, txLeadRate: leadRate);
        var state = StateWithRate(snapshotRate);

        var full = DopplerCatLead.ResolveRangeRates(
            propagator,
            new RigSettings { DopplerCatLeadEnabled = true, CatDelayMs = 100, DopplerCatLeadMs = 0, DopplerCatLeadGainPercent = 100 },
            Site,
            state,
            utc);
        var half = DopplerCatLead.ResolveRangeRates(
            propagator,
            new RigSettings { DopplerCatLeadEnabled = true, CatDelayMs = 100, DopplerCatLeadMs = 0, DopplerCatLeadGainPercent = 50 },
            Site,
            state,
            utc);

        Assert.Equal(leadRate, full.RxRangeRateKmPerSec, 3);
        Assert.Equal(snapshotRate + (leadRate - snapshotRate) * 0.5, half.RxRangeRateKmPerSec, 3);
        Assert.Equal(full.LeadBlend * 0.5, half.LeadBlend, 3);
    }

    private sealed class StubPropagator(
        DateTime resolveUtc,
        double snapshotRate,
        double slopeSampleRate,
        double rxLeadRate,
        double txLeadRate) : IOrbitPropagator
    {
        public int CallCount { get; private set; }
        public DateTime LastSlopeUtc { get; private set; }
        public DateTime LastRxUtc { get; private set; }
        public DateTime LastTxUtc { get; private set; }

        public void Clear() { }
        public void LoadSatellite(SatelliteCatalogEntry entry) { }
        public void RemoveSatellite(string noradId) { }
        public GeoCoordinate GetSubpoint(string noradId, DateTime utc) => new(0, 0, 400);
        public EciPosition GetEciPosition(string noradId, DateTime utc) => new(0, 0, 0);
        public bool HasSatellite(string noradId) => true;
        public IReadOnlyCollection<string> LoadedNoradIds => ["99999"];

        public LookAngles GetLookAngles(string noradId, GroundStation site, DateTime utc)
        {
            CallCount++;
            if (utc == resolveUtc.AddSeconds(DopplerCatLead.RangeRateSlopeSampleSec))
            {
                LastSlopeUtc = utc;
                return new LookAngles(180, 30, 800, slopeSampleRate);
            }

            if (utc == resolveUtc.AddMilliseconds(40))
            {
                LastRxUtc = utc;
                return new LookAngles(180, 30, 800, rxLeadRate);
            }

            if (utc == resolveUtc.AddMilliseconds(50))
            {
                if (LastRxUtc == default)
                {
                    LastRxUtc = utc;
                    return new LookAngles(180, 30, 800, rxLeadRate);
                }

                LastTxUtc = utc;
                return new LookAngles(180, 30, 800, txLeadRate);
            }

            return new LookAngles(180, 30, 800, snapshotRate);
        }
    }

    private static DateTime FindSteepestUtcNearTca(
        PublicOrbitToolsPropagator propagator,
        string noradId,
        GroundStation site,
        DateTime tcaUtc)
    {
        var bestUtc = tcaUtc;
        var bestSlope = double.MinValue;

        for (var offset = -180; offset <= 180; offset += 5)
        {
            var t = tcaUtc.AddSeconds(offset);
            var rr = propagator.GetLookAngles(noradId, site, t).RangeRateKmPerSec;
            var slope = DopplerCatLead.ComputeRangeRateSlopeKmPerSec2(propagator, noradId, site, t, rr);
            if (slope > bestSlope)
            {
                bestSlope = slope;
                bestUtc = t;
            }
        }

        Assert.True(bestSlope >= DopplerCatLead.SteepRangeRateSlopeKmPerSec2,
            $"Expected a steep range-rate leg near TCA (best slope {bestSlope:F4} km/s²).");
        return bestUtc;
    }

    private static (DateTime TcaUtc, double MaxElevationDeg) FindHighElevationPassTcaUtc(
        PublicOrbitToolsPropagator propagator,
        string noradId,
        GroundStation site,
        double minElevationDeg)
    {
        // TLE epoch 26141 ≈ 2026-05-21; scan a few days for the best pass.
        var start = new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc);
        var bestEl = double.MinValue;
        var bestUtc = start;

        for (var i = 0; i < 259_200; i += 15)
        {
            var t = start.AddSeconds(i);
            var look = propagator.GetLookAngles(noradId, site, t);
            if (look.ElevationDeg > bestEl)
            {
                bestEl = look.ElevationDeg;
                bestUtc = t;
            }
        }

        Assert.True(bestEl >= minElevationDeg,
            $"Expected a pass with elevation ≥ {minElevationDeg:F0}° (best el {bestEl:F1}°).");
        return (bestUtc, bestEl);
    }

    private sealed class ThrowingPropagator : IOrbitPropagator
    {
        public void Clear() { }
        public void LoadSatellite(SatelliteCatalogEntry entry) { }
        public void RemoveSatellite(string noradId) { }
        public GeoCoordinate GetSubpoint(string noradId, DateTime utc) => throw new InvalidOperationException();
        public EciPosition GetEciPosition(string noradId, DateTime utc) => throw new InvalidOperationException();
        public bool HasSatellite(string noradId) => true;
        public IReadOnlyCollection<string> LoadedNoradIds => [];
        public LookAngles GetLookAngles(string noradId, GroundStation site, DateTime utc) =>
            throw new InvalidOperationException();
    }
}
