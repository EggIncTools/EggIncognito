using System.Globalization;
using System.Text.Json.Nodes;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class Egg9000ParityTests {
    public static TheoryData<string> Captures => ["egginc", "egginc-android-1.37.so"];

    private static bool TryLoadCapture(string file, out byte[] bin) => CaptureFixture.TryLoad(file, out bin);

    private static ResearchCatalogExtractor.Result Catalog(string capture, byte[] bin) =>
        CaptureFixture.ResearchCatalog(capture, bin);

    private static ResearchPriceExtractor.ByIdResult ResearchPrices(string capture, byte[] bin) =>
        CaptureFixture.ResearchPrices(capture, bin);

    private static BoostEffectExtractor.Result BoostTable(string capture, byte[] bin) =>
        CaptureFixture.BoostTable(capture, bin);

    private static JsonNode? Egg9000Json(string file) {
        string? path = Find(Path.Combine("..", "EGG9000", "EGG9000.Common", "JsonData", file));
        return path is null ? null : JsonNode.Parse(File.ReadAllText(path));
    }

    private static string? Find(string relative) {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null) {
            string candidate = Path.GetFullPath(Path.Combine(dir, relative));
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    private static void AssertNoMismatches(List<string> mismatches, int compared) {
        Assert.True(compared > 0, "nothing compared");
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} of {compared} differ:\n{string.Join('\n', mismatches)}");
    }

    private static bool Close(double a, double b, double rel = 1e-6) =>
        a == b || Math.Abs(a - b) <= rel * Math.Max(Math.Abs(a), Math.Abs(b));

    [Theory]
    [MemberData(nameof(Captures))]
    public void Research_CommonMaxLevelTierAndPrices_MatchEgg9000(string capture) {
        if (!TryLoadCapture(capture, out byte[] bin) || Egg9000Json("researches.json") is not JsonArray known) return;

        var catalog = Catalog(capture, bin);
        Assert.True(catalog.Ok, catalog.Diagnostics);
        var prices = ResearchPrices(capture, bin);
        Assert.True(prices.Ok, prices.Diagnostics);
        var constants = ResearchPriceCurveExtractor.Extract(bin);
        Assert.True(constants.Ok, constants.Diagnostics);
        var curve = ResearchPriceCurve.Build(ResearchPriceCurve.Standard, constants.Constants);
        var byId = catalog.Entries.ToDictionary(e => e.Id, StringComparer.Ordinal);

        var mismatches = new List<string>();
        int compared = 0;
        foreach (var row in known.OfType<JsonObject>()) {
            if (row["type"]?.GetValue<string>() != "common") continue;
            string id = row["id"]!.GetValue<string>();
            if (!byId.TryGetValue(id, out var e)) {
                mismatches.Add($"{id}: missing from extraction");
                continue;
            }

            compared++;
            int levels = row["levels"]!.GetValue<int>();
            int tier = row["tier"]!.GetValue<int>();
            if (e.MaxLevel != levels) mismatches.Add($"{id}: maxLevel {e.MaxLevel} vs {levels}");
            if (e.Tier + 1 != tier) mismatches.Add($"{id}: tier {e.Tier} (0-based) vs {tier}");

            var p = prices.Prices[id];
            var expected = row["prices"]!.AsArray().Select(n => n!.GetValue<double>()).ToList();
            for (int level = 0; level < expected.Count; level++) {
                double got = ResearchPriceCurve.CommonPrice(p.PriceStart, p.PriceEnd, p.PricePercent, p.MaxLevel, level, curve);
                if (Egg9000ResearchPriceErrata.TryGetValue((id, level), out double binary)) {
                    if (!Close(got, binary, 1e-4))
                        mismatches.Add($"{id} L{level}: {got.ToString("R", CultureInfo.InvariantCulture)} vs pinned {binary}");
                    if (Close(expected[level], got, 0.01)) mismatches.Add($"{id} L{level}: erratum no longer needed");
                    continue;
                }

                if (!Close(got, expected[level], 0.01))
                    mismatches.Add($"{id} L{level}: {got.ToString("R", CultureInfo.InvariantCulture)} vs {expected[level]}");
            }
        }

        AssertNoMismatches(mismatches, compared);
    }

    private static readonly HashSet<string> Egg9000OnlyEpicIds = ["nter_hyperloop_trains", "nter_fuel_tank"];

    private static readonly Dictionary<(string Id, int Level), double> Egg9000ResearchPriceErrata = new() {
        [("internal_hatchery1", 9)] = 266696824.9476652,
        [("hatchery_expansion", 3)] = 13283.192204075558,
        [("superfeed", 4)] = 2038961192.6512613,
        [("super_alloy", 39)] = 4.3491455307568255E+24,
        [("genetic_purification", 27)] = 1.5760854118966154E+24,
        [("internal_hatchery5", 233)] = 3.328214306006385E+33,
        [("chrystal_shells", 65)] = 5.2377871566408053E+36,
        [("telepathic_will", 15)] = 3.257535035348384E+39,
        [("neural_net_refine", 20)] = 2.7471616440308633E+60,
        [("matter_reconfig", 186)] = 2.950056653175076E+57,
        [("matter_reconfig", 395)] = 2.4704409037693923E+63
    };

    [Theory]
    [MemberData(nameof(Captures))]
    public void Research_EpicMaxLevelAndCosts_MatchEgg9000(string capture) {
        if (!TryLoadCapture(capture, out byte[] bin) || Egg9000Json("ei-epic-research.json") is not JsonObject root) return;

        var catalog = Catalog(capture, bin);
        Assert.True(catalog.Ok, catalog.Diagnostics);
        var prices = ResearchPrices(capture, bin);
        Assert.True(prices.Ok, prices.Diagnostics);

        var mismatches = new List<string>();
        int compared = 0;
        foreach (var row in root["epicResearchItems"]!.AsArray().OfType<JsonObject>()) {
            if (row["_numLevels"] is null) continue;
            string id = row["id"]!.GetValue<string>();
            if (Egg9000OnlyEpicIds.Contains(id)) continue;
            if (!prices.Prices.TryGetValue(id, out var p)) {
                mismatches.Add($"{id}: missing from extraction");
                continue;
            }

            compared++;
            int levels = row["_numLevels"]!.GetValue<int>();
            if (p.MaxLevel != levels) mismatches.Add($"{id}: maxLevel {p.MaxLevel} vs {levels}");

            var overrides = row["_overrideCosts"]?.AsArray().Select(n => n!.GetValue<int>()).ToList();
            int first = row["_firstCost"]!.GetValue<int>();
            int last = row["_lastCost"]!.GetValue<int>();
            for (int level = 0; level < levels; level++) {
                int want = overrides is { Count: > 0 }
                    ? overrides[level]
                    : levels == 1 ? first : first + (int)(level * (double)(last - first) / (levels - 1));
                int got = levels == 1
                    ? Math.Max(1, (int)p.EpicPriceStart)
                    : ResearchPriceCurve.EpicPrice(p.EpicPriceStart, p.EpicPriceEnd, p.MaxLevel, level);
                if (got != want) mismatches.Add($"{id} L{level}: {got} vs {want}");
            }
        }

        AssertNoMismatches(mismatches, compared);
    }

    [Theory]
    [MemberData(nameof(Captures))]
    public void Eggs_ValueMatchesEgg9000(string capture) {
        if (!TryLoadCapture(capture, out byte[] bin) || Egg9000Json("ei-statics.json") is not JsonObject root) return;

        var eggs = EggCatalogExtractor.ExtractAuto(bin);
        Assert.True(eggs.Ok, eggs.Diagnostics);

        var mismatches = new List<string>();
        int compared = 0;
        if (root["eggIncEggs"]!.AsArray().OfType<JsonObject>().Count(r => r["value"] is not null) < 19) return;
        foreach (var row in root["eggIncEggs"]!.AsArray().OfType<JsonObject>()) {
            int id = row["id"]!.GetValue<int>();
            if (id is < 1 or > 19 || row["value"] is null) continue;
            compared++;
            double want = row["value"]!.GetValue<double>();
            var got = eggs.Entries.FirstOrDefault(e => e.Index == id - 1);
            if (got.Name is null) mismatches.Add($"egg {id}: missing from extraction");
            else if (!Close(got.BaseValue, want)) mismatches.Add($"egg {id} {got.Name}: {got.BaseValue} vs {want}");
        }

        AssertNoMismatches(mismatches, compared);
    }

    [Theory]
    [MemberData(nameof(Captures))]
    public void Habs_CapacityMatchesEgg9000(string capture) {
        if (!TryLoadCapture(capture, out byte[] bin)) return;

        var habs = HabCatalogExtractor.Extract(bin);
        Assert.True(habs.Ok, habs.Diagnostics);
        double[] known = [250, 500, 1e3, 2e3, 5e3, 1e4, 2e4, 5e4, 1e5, 2e5, 5e5, 1e6, 2e6, 5e6, 1e7, 2.5e7, 5e7, 1e8, 6e8];

        var mismatches = new List<string>();
        for (int i = 0; i < known.Length; i++) {
            var got = habs.Entries.FirstOrDefault(e => e.Index == i);
            if (got.Name is null) mismatches.Add($"hab {i}: missing from extraction");
            else if (!Close(got.Capacity, known[i])) mismatches.Add($"hab {i} {got.Name}: {got.Capacity} vs {known[i]}");
        }

        AssertNoMismatches(mismatches, known.Length);
    }

    private const string BoostDurationEvidence =
        "boostmanager init stores the duration double at record+0x70; iOS 0x10071d67c jimbos_blue_v2 = 1200 and "
        + "Android 0xd8bf2c agree. EGG9000 carries the pre-v2 live durations for the _v2 and _big records.";

    private const string BoostMagnitudeEvidence =
        "boostmanager init stores the magnitude double at record+0x80; iOS 0x10071e9e0 movz 0x4008 = 3 lands at "
        + "jimbos_blue_big+0x80, Android agrees";

    private const string InstantCashEvidence =
        "dimension 10 bd-cash records store the magnitude the game multiplies by; iOS 0x10071d9c0 subsidy_application = 0.1, "
        + "0x10071da88 blank_check = 1, 0x10071db3c money_printer = 5. EGG9000's 10/100/500 is its own display scale.";

    private static readonly Dictionary<string, (double? Minutes, double? Value, string Evidence)> Egg9000BoostErrata =
        new(StringComparer.Ordinal) {
            ["jimbos_blue_v2"] = (20, null, BoostDurationEvidence),
            ["jimbos_blue_big"] = (240, 3, BoostMagnitudeEvidence),
            ["subsidy_application"] = (null, 0.1, InstantCashEvidence),
            ["blank_check"] = (null, 1, InstantCashEvidence),
            ["money_printer"] = (null, 5, InstantCashEvidence),
            ["jimbos_purple_v2"] = (15, null, BoostDurationEvidence),
            ["jimbos_purple_big"] = (180, null, BoostDurationEvidence),
            ["tachyon_prism_blue_v2"] = (10, null, BoostDurationEvidence),
            ["tachyon_prism_purple_v2"] = (10, null, BoostDurationEvidence),
            ["soul_beacon_blue_v2"] = (30, null, BoostDurationEvidence),
            ["soul_beacon_purple_v2"] = (20, null, BoostDurationEvidence)
        };

    [Theory]
    [MemberData(nameof(Captures))]
    public void Boosts_MultiplierAndDurationMatchEgg9000(string capture) {
        if (!TryLoadCapture(capture, out byte[] bin) || Egg9000Json("ei-statics.json") is not JsonObject root) return;

        var boosts = BoostTable(capture, bin);
        Assert.True(boosts.Ok, boosts.Diagnostics);
        var byId = boosts.Entries.ToDictionary(e => e.Id, StringComparer.Ordinal);

        var mismatches = new List<string>();
        int compared = 0;
        if (root["eggIncBoosts"]!.AsArray().OfType<JsonObject>().All(r => r["timeMinutesInt"] is null)) return;
        foreach (var row in root["eggIncBoosts"]!.AsArray().OfType<JsonObject>()) {
            if (row["timeMinutesInt"] is null || row["value"] is null) continue;
            string id = row["id"]!.GetValue<string>();
            if (!byId.TryGetValue(id, out var e)) {
                mismatches.Add($"{id}: missing from extraction");
                continue;
            }

            compared++;
            double minutes = row["timeMinutesInt"]!.GetValue<double>();
            double value = row["value"]!.GetValue<double>();
            if (Egg9000BoostErrata.TryGetValue(id, out var fix)) {
                if (fix.Minutes is { } m) {
                    if (!Close(e.DurationSeconds, m * 60)) mismatches.Add($"{id}: duration {e.DurationSeconds}s vs pinned {m}min");
                    if (Close(minutes, m)) mismatches.Add($"{id}: duration erratum no longer needed");
                    minutes = m;
                }

                if (fix.Value is { } v) {
                    if (!Close(e.Magnitude, v)) mismatches.Add($"{id}: magnitude {e.Magnitude} vs pinned {v}");
                    if (Close(value, v)) mismatches.Add($"{id}: magnitude erratum no longer needed");
                    value = v;
                }
            }

            if (!Close(e.DurationSeconds, minutes * 60)) mismatches.Add($"{id}: duration {e.DurationSeconds}s vs {minutes}min");
            if (value != 0 && !Close(e.Magnitude, value)) mismatches.Add($"{id}: magnitude {e.Magnitude} vs {value}");
        }

        AssertNoMismatches(mismatches, compared);
    }

    [Fact]
    public void Boosts_IosAndAndroidTablesAreIdentical() {
        if (!TryLoadCapture("egginc", out byte[] ios) || !TryLoadCapture("egginc-android-1.37.so", out byte[] android))
            return;

        var a = BoostTable("egginc", ios);
        var b = BoostTable("egginc-android-1.37.so", android);
        Assert.True(a.Ok, a.Diagnostics);
        Assert.True(b.Ok, b.Diagnostics);
        List<(string, int, double, double)> rows = [.. a.Entries.Select(e => (e.Id, e.Dimension, e.DurationSeconds, e.Magnitude)).Order()];
        List<(string, int, double, double)> other = [.. b.Entries.Select(e => (e.Id, e.Dimension, e.DurationSeconds, e.Magnitude)).Order()];
        Assert.Equal(rows, other);
        Assert.Equal(33, rows.Count);
    }

    private const int PercentDimension = 12;
    private const int CountDimension = 20;
    private const int RunningChickenDimension = 24;
    private const int ProphecyDimension = 26;
    private const int ClarityDimension = 29;

    private const string LunarTotemEvidence =
        "android 1.37 artifactdata init 0xc478e8 builds lunar_totem magnitudes 2,4,8,20,40,50,100,150,200; "
        + "EGG9000 stores the displayed Nx instead of multiplier-1 from 8x up, unlike light-of-eggendil 100x as 99";

    private static readonly Dictionary<(int AfxId, int Level, int Rarity), (double Egg9000, double Delta, string Evidence)>
        Egg9000ArtifactErrata = new() {
            [(0, 1, 1)] = (8, 7, LunarTotemEvidence),
            [(0, 2, 0)] = (20, 19, LunarTotemEvidence),
            [(0, 2, 1)] = (40, 39, LunarTotemEvidence),
            [(0, 3, 0)] = (50, 49, LunarTotemEvidence),
            [(0, 3, 1)] = (100, 99, LunarTotemEvidence),
            [(0, 3, 2)] = (150, 149, LunarTotemEvidence),
            [(0, 3, 3)] = (200, 199, LunarTotemEvidence)
        };

    private static double Egg9000Delta(int dimension, ArtifactTableExtractor.RarityEffect e) => dimension switch {
        PercentDimension or CountDimension or RunningChickenDimension or ClarityDimension => e.Magnitude,
        ProphecyDimension => TemplatePercent(e.Template) / 100,
        _ => e.Magnitude == 0 ? 0 : e.Magnitude - 1
    };

    private static double TemplatePercent(string template) {
        string plain = new([.. template.Where(c => char.IsDigit(c) || c is '.' or '%')]);
        return double.Parse(plain[..plain.IndexOf('%')], CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Artifacts_EffectAndSlots_MatchEgg9000() {
        if (!TryLoadCapture("egginc-android-1.37.so", out byte[] bin) || Egg9000Json("eiafx-data.json") is not JsonObject root)
            return;

        var table = ArtifactTableExtractor.Extract(bin);
        Assert.True(table.Ok, table.Diagnostics);
        var ours = table.Families
            .SelectMany(f => f.Tiers.SelectMany(t => t.Effects.Select(e => (f, t, e))))
            .ToDictionary(x => (x.f.AfxId, x.t.Level, x.e.Rarity), x => (x.f.Dimension, x.e));

        var mismatches = new List<string>();
        int compared = 0;
        int expected = 0;
        foreach (var family in root["artifact_families"]?.AsArray().OfType<JsonObject>() ?? []) {
            int afxId = family["afx_id"]?.GetValue<int>() ?? -1;
            foreach (var tier in family["tiers"]?.AsArray().OfType<JsonObject>() ?? []) {
                int level = tier["afx_level"]?.GetValue<int>() ?? -1;
                foreach (var eff in tier["effects"]?.AsArray().OfType<JsonObject>() ?? []) {
                    expected++;
                    int rarity = eff["afx_rarity"]?.GetValue<int>() ?? -1;
                    string where = $"{family["id"]} afx {afxId} L{level} R{rarity}";
                    if (!ours.TryGetValue((afxId, level, rarity), out var hit)) {
                        mismatches.Add($"{where}: missing from extraction");
                        continue;
                    }

                    compared++;
                    int slots = eff["slots"]?.GetValue<int>() ?? 0;
                    double? delta = eff["effect_delta"]?.GetValue<double>();
                    if (Egg9000ArtifactErrata.TryGetValue((afxId, level, rarity), out var fix)) {
                        if (delta != fix.Egg9000) mismatches.Add($"{where}: stale erratum, EGG9000 now {delta}");
                        delta = fix.Delta;
                    }
                    double got = Egg9000Delta(hit.Dimension, hit.e);
                    if (hit.e.Slots != slots) mismatches.Add($"{where}: slots {hit.e.Slots} vs {slots}");
                    if (delta is not { } d || !Close(got, d))
                        mismatches.Add($"{where}: dim {hit.Dimension} magnitude {hit.e.Magnitude} delta {got} vs {delta}");
                }
            }
        }

        AssertNoMismatches(mismatches, compared);
        Assert.Equal(expected, compared);
    }

    [Fact]
    public void MissionShips_MatchEgg9000Config() {
        var ours = Find(Path.Combine("EggIncognito", "Endpoints", "default", "ei_afx", "config.json"));
        if (ours is null || Egg9000Json("eiafx-config.json") is not JsonObject known) return;

        var doc = Core.Services.GameDataDocBuilders.BuildMissionShipsFromJson(File.ReadAllText(ours), "parity");
        var ships = JsonNode.Parse(doc.Json)!["ships"]!.AsArray();
        var mismatches = new List<string>();
        int compared = 0;
        foreach (var k in known["missionParameters"]!.AsArray().OfType<JsonObject>()) {
            string ship = k["ship"]!.GetValue<string>();
            var s = ships.FirstOrDefault(x => x!["ship"]!.GetValue<string>() == ship);
            if (s is null) {
                mismatches.Add($"{ship}: missing from mission-ships");
                continue;
            }

            foreach (var d in k["durations"]!.AsArray().OfType<JsonObject>()) {
                compared++;
                string type = d["durationType"]!.GetValue<string>();
                var g = s["durations"]!.AsArray().FirstOrDefault(x => x!["type"]!.GetValue<string>() == type);
                if (g is null) {
                    mismatches.Add($"{ship} {type}: missing");
                    continue;
                }

                foreach (var (theirs, mine) in new[] {
                             ("seconds", "seconds"), ("capacity", "capacity"), ("levelCapacityBump", "levelCapacityBump"),
                             ("quality", "quality"), ("minQuality", "minQuality"), ("maxQuality", "maxQuality"),
                             ("levelQualityBump", "levelQualityBump")
                         }) {
                    double a = d[theirs]?.GetValue<double>() ?? 0;
                    double b = g[mine]?.GetValue<double>() ?? 0;
                    if (!Close(a, b)) mismatches.Add($"{ship} {type} {theirs}: {b} vs {a}");
                }
            }

            var req = k["levelMissionRequirements"]?.AsArray().Select(n => n!.GetValue<int>()).ToArray() ?? [];
            var mineReq = s["levelMissionRequirements"]?.AsArray().Select(n => n!.GetValue<int>()).ToArray() ?? [];
            if (!req.SequenceEqual(mineReq))
                mismatches.Add($"{ship} levelMissionRequirements: [{string.Join(',', mineReq)}] vs [{string.Join(',', req)}]");
        }

        AssertNoMismatches(mismatches, compared);
    }
}
