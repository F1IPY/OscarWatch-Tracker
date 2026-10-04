using OscarWatch.Core.Models;
using OscarWatch.Core.Services;
using OscarWatch.Localization;
using OscarWatch.ViewModels;
using static OscarWatch.Localization.LocalizationCulture;

namespace OscarWatch.Tests;

public class SatellitePickerViewModelTests
{
    [Fact]
    public void Load_checks_satellites_that_are_enabled_only_via_fuzzy_name_match()
    {
        using var _ = TestUiCulture.Apply(DefaultLanguage);

        var so50 = Entry("SAUDISAT 1C (SO-50)", "27607");
        var iss = Entry("ISS", "25544");
        var settings = NameOnlySettings(["SO-50"]);
        var tle = new StubTleService([so50, iss]);

        Assert.Equal([so50], tle.GetEnabledSatellites(settings.Current));

        var vm = new SatellitePickerViewModel(settings, tle, LocalizationService.Instance);

        Assert.True(Assert.Single(vm.Satellites, s => s.NoradId == "27607").IsEnabled);
        Assert.False(Assert.Single(vm.Satellites, s => s.NoradId == "25544").IsEnabled);
    }

    [Fact]
    public void Load_checks_short_catalog_name_when_enabled_list_has_longer_containing_name()
    {
        using var _ = TestUiCulture.Apply(DefaultLanguage);

        var isat = Entry("ISAT", "43879");
        var origami = Entry("OrigamiSat 2", "68795");
        var settings = NameOnlySettings(["ISAT (CUBE)", "OrigamiSat 2"]);
        var tle = new StubTleService([isat, origami]);

        Assert.Equal(2, tle.GetEnabledSatellites(settings.Current).Count);

        var vm = new SatellitePickerViewModel(settings, tle, LocalizationService.Instance);

        Assert.True(Assert.Single(vm.Satellites, s => s.Name == "ISAT").IsEnabled);
        Assert.True(Assert.Single(vm.Satellites, s => s.Name == "OrigamiSat 2").IsEnabled);
    }

    [Fact]
    public void OrigamiSat_2_alone_does_not_show_ISAT_as_enabled()
    {
        using var _ = TestUiCulture.Apply(DefaultLanguage);

        var isat = Entry("ISAT", "43879");
        var origami = Entry("OrigamiSat 2", "68795");
        var settings = NameOnlySettings(["OrigamiSat 2"]);
        var tle = new StubTleService([isat, origami]);

        Assert.Equal([origami], tle.GetEnabledSatellites(settings.Current));

        var vm = new SatellitePickerViewModel(settings, tle, LocalizationService.Instance);

        Assert.False(Assert.Single(vm.Satellites, s => s.Name == "ISAT").IsEnabled);
        Assert.True(Assert.Single(vm.Satellites, s => s.Name == "OrigamiSat 2").IsEnabled);
    }

    [Fact]
    public void Load_checks_by_norad_id_when_catalog_name_differs()
    {
        using var _ = TestUiCulture.Apply(DefaultLanguage);

        var origami = Entry("OrigamiSat 2", "68795");
        var settings = new TestSettingsService();
        settings.Current.EnabledSatelliteNames = ["ORIGAMISAT-2"];
        settings.Current.EnabledSatelliteNoradIds = ["68795"];
        var tle = new StubTleService([origami]);

        var vm = new SatellitePickerViewModel(settings, tle, LocalizationService.Instance);

        Assert.True(Assert.Single(vm.Satellites).IsEnabled);
        Assert.Equal([origami], tle.GetEnabledSatellites(settings.Current));
    }

    [Fact]
    public void Selected_filter_excludes_unchecked_satellites_from_the_list()
    {
        using var _ = TestUiCulture.Apply(DefaultLanguage);

        var ao73 = Entry("AO-73", "39444");
        var fo29 = Entry("FO-29", "24278");
        var iss = Entry("ISS", "25544");
        var settings = NameOnlySettings(["AO-73", "FO-29"]);
        var tle = new StubTleService([ao73, fo29, iss]);
        var vm = new SatellitePickerViewModel(settings, tle, LocalizationService.Instance);

        vm.SelectedSelectionFilterChoice = vm.SelectionFilterChoices
            .Single(c => c.Value == SatellitePickerSelectionFilter.Selected);

        Assert.Equal(["AO-73", "FO-29"], vm.FilteredSatellites.Select(s => s.Name).ToList());
        Assert.Equal(3, vm.Satellites.Count);
    }

    [Fact]
    public void Search_filter_excludes_non_matching_satellites_from_the_list()
    {
        using var _ = TestUiCulture.Apply(DefaultLanguage);

        var ao73 = Entry("AO-73", "39444");
        var fo29 = Entry("FO-29", "24278");
        var settings = NameOnlySettings([]);
        var tle = new StubTleService([ao73, fo29]);
        var vm = new SatellitePickerViewModel(settings, tle, LocalizationService.Instance);

        vm.SearchText = "AO-73";

        Assert.Equal(["AO-73"], vm.FilteredSatellites.Select(s => s.Name).ToList());
        Assert.Equal(2, vm.Satellites.Count);
    }

    [Fact]
    public async Task Save_writes_both_names_and_normalised_norad_ids()
    {
        using var _ = TestUiCulture.Apply(DefaultLanguage);

        var so50 = Entry("SAUDISAT 1C (SO-50)", "27607");
        var alpha = Entry("HIGH-CAT", "A0000");
        var settings = NameOnlySettings(["SO-50"]);
        var tle = new StubTleService([so50, alpha]);
        var vm = new SatellitePickerViewModel(settings, tle, LocalizationService.Instance);

        Assert.Single(vm.Satellites, s => s.NoradId == "A0000").IsEnabled = true;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["HIGH-CAT", "SAUDISAT 1C (SO-50)"], settings.Current.EnabledSatelliteNames.OrderBy(n => n).ToList());
        Assert.Equal(["27607", "A0000"], settings.Current.EnabledSatelliteNoradIds.OrderBy(id => id).ToList());
    }

    [Fact]
    public async Task Save_rewrites_fuzzy_enabled_names_to_current_catalog_names()
    {
        using var _ = TestUiCulture.Apply(DefaultLanguage);

        var so50 = Entry("SAUDISAT 1C (SO-50)", "27607");
        var settings = NameOnlySettings(["SO-50"]);
        var tle = new StubTleService([so50]);
        var vm = new SatellitePickerViewModel(settings, tle, LocalizationService.Instance);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["SAUDISAT 1C (SO-50)"], settings.Current.EnabledSatelliteNames);
        Assert.Equal(["27607"], settings.Current.EnabledSatelliteNoradIds);
    }

    [Fact]
    public async Task Save_unchecking_removes_fuzzy_enabled_satellite()
    {
        using var _ = TestUiCulture.Apply(DefaultLanguage);

        var isat = Entry("ISAT", "43879");
        var origami = Entry("OrigamiSat 2", "68795");
        var settings = NameOnlySettings(["ISAT (CUBE)", "OrigamiSat 2"]);
        var tle = new StubTleService([isat, origami]);
        var vm = new SatellitePickerViewModel(settings, tle, LocalizationService.Instance);

        Assert.Single(vm.Satellites, s => s.Name == "ISAT").IsEnabled = false;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["OrigamiSat 2"], settings.Current.EnabledSatelliteNames);
        Assert.Equal(["68795"], settings.Current.EnabledSatelliteNoradIds);
        Assert.Equal([origami], tle.GetEnabledSatellites(settings.Current));
    }

    [Fact]
    public async Task Save_with_empty_catalogue_keeps_existing_selection()
    {
        using var _ = TestUiCulture.Apply(DefaultLanguage);

        var settings = new TestSettingsService();
        settings.Current.EnabledSatelliteNames = ["AO-07", "FO-29"];
        settings.Current.EnabledSatelliteNoradIds = ["07530", "24278"];
        var vm = new SatellitePickerViewModel(settings, new StubTleService([]), LocalizationService.Instance);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["AO-07", "FO-29"], settings.Current.EnabledSatelliteNames);
        Assert.Equal(["07530", "24278"], settings.Current.EnabledSatelliteNoradIds.OrderBy(id => id).ToList());
    }

    [Fact]
    public async Task Save_keeps_selection_for_satellite_missing_from_catalogue_and_applies_changes_to_loaded_ones()
    {
        using var _ = TestUiCulture.Apply(DefaultLanguage);

        var fo29 = Entry("FO-29", "24278");
        var iss = Entry("ISS", "25544");
        var settings = new TestSettingsService();
        settings.Current.EnabledSatelliteNames = ["AO-07", "FO-29"];
        settings.Current.EnabledSatelliteNoradIds = ["07530", "24278"];
        var vm = new SatellitePickerViewModel(settings, new StubTleService([fo29, iss]), LocalizationService.Instance);

        Assert.Single(vm.Satellites, s => s.Name == "FO-29").IsEnabled = false;
        Assert.Single(vm.Satellites, s => s.Name == "ISS").IsEnabled = true;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["AO-07", "ISS"], settings.Current.EnabledSatelliteNames.OrderBy(n => n).ToList());
        Assert.Equal(["07530", "25544"], settings.Current.EnabledSatelliteNoradIds.OrderBy(id => id).ToList());
    }

    private static TestSettingsService NameOnlySettings(List<string> names)
    {
        var settings = new TestSettingsService();
        settings.Current.EnabledSatelliteNames = names;
        settings.Current.EnabledSatelliteNoradIds = [];
        return settings;
    }

    private static SatelliteCatalogEntry Entry(string name, string noradId) => new()
    {
        Name = name,
        NoradId = noradId,
        Line1 = $"1 {noradId}U 00000A   00001.00000000  .00000000  00000-0  00000-0 0  0000",
        Line2 = $"2 {noradId}  00.0000 000.0000 0000000 000.0000 000.0000 15.00000000 00000",
        EpochUtc = DateTime.UtcNow
    };

    private sealed class TestSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public string? LoadError => null;
        public bool CanPersist => true;
        public string SettingsPath { get; } = Path.Combine(Path.GetTempPath(), "oscarwatch-picker-test-settings.json");
        public string SerializeCurrent() => "{}";
        public Task ReplaceAndSaveAsync(AppSettings imported, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Load() { }
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void RequestSave() { }
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void SyncGridFromLatLon() { }
        public void SyncLatLonFromGrid() { }
        public void EnsureSavedStations() { }
        public void ApplyActiveStation() { }
        public void SyncActiveStationFromGroundStation() { }
    }

    private sealed class StubTleService(IReadOnlyList<SatelliteCatalogEntry> catalog) : ITleService
    {
        public IReadOnlyList<SatelliteCatalogEntry> Catalog => catalog;
        public DateTime? LastFetchedUtc => DateTime.UtcNow;
        public string CachePath => "";
        public TleCatalogLoadDiagnostics? LastLoadDiagnostics => null;
        public bool IsStale(int staleHours) => false;
        public Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void InvalidateCatalog() { }
        public string ActiveSourceLabel => "Test";

        public IReadOnlyList<SatelliteCatalogEntry> GetEnabledSatellites(AppSettings settings)
        {
            var ids = SatelliteCatalogMatching.CreateNoradIdSet(settings.EnabledSatelliteNoradIds);
            var names = new HashSet<string>(settings.EnabledSatelliteNames ?? [], StringComparer.OrdinalIgnoreCase);
            return catalog.Where(s => SatelliteCatalogMatching.IsEnabled(s, ids, names)).ToList();
        }
    }
}
