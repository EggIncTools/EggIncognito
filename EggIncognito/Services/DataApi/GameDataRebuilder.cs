using System.Text;
using EggSharp.GameData.Artifacts;
using EggIncognito.Core;
using EggIncognito.Core.Services;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Services;
using EggSharp.GameData;
using EggIncognito.Services.Feed;
using EggIncognito.Services.EggSharpSender;
using EggIncognito.Services.Feed.Kinds;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.DataApi;

public sealed record RebuildDocResult(string Id, string Status, int? Count, int? Bytes, string? Note);

public sealed class GameDataRebuilder(
    IServiceProvider services,
    GameBinaryProvider binaries,
    GameDataStore gameDataStore,
    ILogger<GameDataRebuilder> logger,
    TimeProvider time,
    FeedPublisher? publisher = null,
    EggIncognitoDbContext? dbContext = null,
    EggSharpDataPusher? eggSharp = null) {
    private string _inputSha = "";
    private readonly List<string> _changedDocs = [];
    private string _binaryVersion = "";
    private string _platform = "";
    private string? _prevBinaryVersion;

    private static readonly string[] BinaryDocIds = [
        "boost-catalog", "boosts", "missions", "eggs", "vehicles", "dimensions", "research", "habs", "artifacts",
        FarmPlacementCatalog.DocumentId, ArtifactDefinitionsId
    ];

    private sealed record Candidate(string Platform, string Version, byte[] Bin,
        IReadOnlyList<MachoSymbols.Symbol> Syms, IReadOnlyList<MachoSections.Section> Sections, bool IsElf);

    public Task<(IReadOnlyList<RebuildDocResult> Results, string? BinaryNote)> RebuildAsync(CancellationToken ct) =>
        RebuildAsync(false, ct);

    public async Task<(IReadOnlyList<RebuildDocResult> Results, string? BinaryNote)> RebuildAsync(bool force,
        CancellationToken ct) {
        var results = new List<RebuildDocResult>();
        _changedDocs.Clear();
        _binaryVersion = "";
        _platform = "";
        _prevBinaryVersion = null;

        string inputSha = await BinaryInputShaAsync(ct);
        var stored = await StoredInputShasAsync(ct);
        _inputSha = inputSha;

        bool allCurrent = inputSha.Length > 0 && BinaryDocIds.All(id =>
            string.Equals(stored.GetValueOrDefault(id), inputSha, StringComparison.Ordinal));
        if (!force && allCurrent) {
            string shortSha = Hashes.Short(inputSha, 12);
            foreach (string id in BinaryDocIds)
                results.Add(new RebuildDocResult(id, "current", null, null, $"inputs unchanged ({shortSha})"));
            await LandColleggtiblesAsync(results, ct);
            await LandArtifactCatalogAsync(results, ct);
            await LandMissionShipsAsync(results, ct);
            return (results, $"inputs unchanged ({shortSha})");
        }

        var found = await binaries.GetExtractionCandidatesAsync(ct);
        var candidates = found.Candidates.Select(c => {
            bool elf = ElfSections.IsElf64Le(c.Bytes);
            var syms = c.Symbols ?? (elf ? [] : MachoSymbols.Read(c.Bytes));
            var sections = elf ? [] : MachoSections.Read(c.Bytes);
            return new Candidate(c.Platform, c.Version, c.Bytes, syms, sections, elf);
        }).ToList();

        if (candidates.Count == 0) {
            string why = found.Rejected.Count == 0 ? "no extraction binary available" : found.Diagnostics;
            logger.LogWarning("gamedata rebuild found no extraction binary: {Why}", why);
            foreach (string id in BinaryDocIds) results.Add(new RebuildDocResult(id, "skipped", null, null, why));
        } else {
            string configJson = await services.GetRequiredService<EndpointStore>()
                .CurrentJsonAsync(DataCatalog.ConfigRoute, ct) ?? "{}";
            await LandBestAsync(results, "boost-catalog", candidates, c => {
                var built = BoostCatalogBuilder.Build(c.Bin, c.Syms, c.Sections, configJson, c.Version);
                string? note = built.MissingCosts.Count > 0
                    ? $"{built.MissingCosts.Count} ids without costs: {string.Join(", ", built.MissingCosts)}"
                    : null;
                return built.File.Boosts.Count == 0
                    ? null
                    : (BoostCatalogBuilder.Serialize(built.File), built.File.Boosts.Count, note);
            }, ct);

            await LandBestAsync(results, "boosts", candidates, c => {
                var r = BoostEffectExtractor.ExtractWith(c.Bin, c.Syms, c.Sections);
                if (!r.Ok || r.Entries.Count == 0) return null;
                var doc = GameDataDocBuilders.BuildBoosts(r.Entries, c.Version);
                return (doc.Json, doc.Count, SkipNote(doc.Skipped, "unmapped"));
            }, ct);

            await LandBestAsync(results, "artifacts", candidates, c => {
                if (!c.IsElf) return null;
                var r = ArtifactTableExtractor.ExtractWith(c.Bin, c.Syms);
                if (!r.Ok) throw new InvalidOperationException(r.Diagnostics);
                var doc = GameDataDocBuilders.BuildArtifacts(r.Families, c.Version);
                return (doc.Json, doc.Count, SkipNote(doc.Skipped, "unmapped"));
            }, ct);

            await LandBestAsync(results, "missions", candidates, c => {
                var r = MissionCatalogExtractor.ExtractAuto(c.Bin);
                if (!r.Ok || r.Entries.Count == 0) return null;
                var doc = GameDataDocBuilders.BuildMissions(r.Entries, c.Version);
                return (doc.Json, doc.Count, SkipNote(doc.Skipped, "goal-less"));
            }, ct);

            await LandBestAsync(results, "eggs", candidates, c => {
                var r = EggCatalogExtractor.ExtractAuto(c.Bin);
                if (!r.Ok || r.Entries.Count == 0) return null;
                var doc = GameDataDocBuilders.BuildEggs(r.Entries, c.Version);
                return (doc.Json, doc.Count, null);
            }, ct);

            await LandBestAsync(results, "vehicles", candidates, c => {
                var r = VehicleCatalogExtractor.ReadWith(c.Bin, c.Syms);
                if (!r.Ok || r.Entries.Count == 0) return null;
                var doc = GameDataDocBuilders.BuildVehicles(r.Entries, c.Version);
                return (doc.Json, doc.Count, SkipNote(doc.Skipped, "nameless"));
            }, ct);

            await LandBestAsync(results, "dimensions", candidates, c => {
                var r = DimensionCatalogExtractor.ExtractWith(c.Bin, c.Syms);
                if (!r.Ok || r.Ids.Count == 0) return null;
                var doc = GameDataDocBuilders.BuildDimensions(r.Ids, c.Version);
                return (doc.Json, doc.Count, null);
            }, ct);

            await LandBestAsync(results, "research", candidates, c => {
                var r = ResearchCatalogExtractor.ExtractAuto(c.Bin);
                if (!r.Ok || r.Entries.Count == 0) return null;
                var prices = ResearchPriceExtractor.ExtractById(c.Bin, r.Entries);
                var curve = ResearchPriceCurveExtractor.Extract(c.Bin);
                var doc = GameDataDocBuilders.BuildResearch(r.Entries, c.Version, prices.Ok ? prices.Prices : null,
                    curve.Ok ? curve.Constants : null);
                string? priceNote = !prices.Ok ? "prices: " + prices.Diagnostics
                    : !curve.Ok ? "price curve: " + curve.Diagnostics : null;
                string? note = SkipNote(doc.Skipped, "undecoded");
                return (doc.Json, doc.Count, note is null ? priceNote : priceNote is null ? note : $"{note}; {priceNote}");
            }, ct);

            await LandBestAsync(results, "habs", candidates, c => {
                var r = HabCatalogExtractor.ExtractWith(c.Bin, c.Syms, c.Sections);
                if (!r.Ok || r.Entries.Count == 0) return null;
                var doc = GameDataDocBuilders.BuildHabs(r.Entries, c.Version);
                return (doc.Json, doc.Count, SkipNote(doc.Skipped, "nameless"));
            }, ct);

            await LandBestAsync(results, FarmPlacementCatalog.DocumentId, candidates, c => {
                var habs = HabCatalogExtractor.ExtractWith(c.Bin, c.Syms, c.Sections);
                var eggs = EggCatalogExtractor.ExtractAuto(c.Bin);
                var vehicles = VehicleCatalogExtractor.ReadWith(c.Bin, c.Syms);
                if (!habs.Ok || !eggs.Ok || !vehicles.Ok) return null;

                var placement = FarmPlacementExtractor.Extract(c.Bin, habs.Entries, eggs.Entries, vehicles.Entries,
                    c.Version);
                if (!placement.Ok) throw new InvalidOperationException(placement.Diagnostics);

                var doc = GameDataDocBuilders.BuildFarmPlacement(placement.Data, c.Version);
                return (doc.Json, doc.Count, SkipNote(doc.Skipped, "nameless"));
            }, ct);
        }

        await LandArtifactDefinitionsAsync(results, candidates, ct);
        await LandColleggtiblesAsync(results, ct);
        await LandArtifactCatalogAsync(results, ct);
        await LandMissionShipsAsync(results, ct);

        string? note = candidates.Count == 0
            ? found.Rejected.Count == 0 ? "no extraction binary available" : found.Diagnostics
            : "binaries " + string.Join(", ", candidates.Select(c => $"{c.Platform} {c.Version}"));

        LogUnbuilt(results);
        await DispatchRebuiltAsync(ct);
        if (_changedDocs.Count > 0) eggSharp?.Request();
        return (results, note);
    }

    public const string ArtifactDefinitionsId = "artifact-definitions";

    private async Task LandArtifactDefinitionsAsync(List<RebuildDocResult> results, IReadOnlyList<Candidate> candidates,
        CancellationToken ct) {
        var c = candidates.FirstOrDefault(x => x.IsElf);
        if (c is null) {
            results.Add(new RebuildDocResult(ArtifactDefinitionsId, "skipped", null, null, "no Android candidate"));
            return;
        }

        string json;
        int count;
        try {
            var table = ArtifactTableExtractor.ExtractWith(c.Bin, c.Syms);
            if (!table.Ok) throw new InvalidOperationException(table.Diagnostics);
            var labels = DimensionLabelExtractor.ExtractWith(c.Bin, c.Syms);
            json = ArtifactDefinitionsBuilder.Build(table, labels, c.Version);
            count = ArtifactDefinitions.Parse(json).Families.Count;
        } catch (Exception ex) {
            results.Add(new RebuildDocResult(ArtifactDefinitionsId, "failed", null, null, ex.Message));
            return;
        }

        try {
            await UpsertAsync(ArtifactDefinitionsId, json, ct);
        } catch (Exception ex) {
            results.Add(new RebuildDocResult(ArtifactDefinitionsId, "failed", count, null, "db write failed: " + ex.Message));
            return;
        }

        results.Add(new RebuildDocResult(ArtifactDefinitionsId, "built", count, Encoding.UTF8.GetByteCount(json),
            $"{c.Platform} {c.Version}"));
    }

    private Task DispatchRebuiltAsync(CancellationToken ct) {
        if (_changedDocs.Count == 0 || publisher is null) return Task.CompletedTask;
        string dedup = _inputSha.Length > 0
            ? _inputSha
            : Hashes.Sha256Hex(string.Join('\n', _changedDocs));
        return publisher.PublishAsync(new GameDataRebuiltEvent(
            _binaryVersion, _prevBinaryVersion, _platform, dedup, [.. _changedDocs], publisher.PageUrl("data")), ct);
    }

    private Task LandColleggtiblesAsync(List<RebuildDocResult> results, CancellationToken ct) =>
        LandAsync(results, "colleggtibles", () => {
            var live = LiveColleggtibleSource.Derive(services, DataCatalog.PeriodicalsRoute)
                       ?? throw new InvalidOperationException("no captured get_periodicals to derive from");
            return (live.Json, live.Extract.Eggs.Count, null);
        }, ct);

    private async Task LandArtifactCatalogAsync(List<RebuildDocResult> results, CancellationToken ct) {
        string afx = await services.GetRequiredService<EndpointStore>()
                         .CurrentJsonAsync(DataCatalog.AfxConfigRoute, ct)
                     ?? throw new InvalidOperationException("no captured ei_afx/config to decode");
        await LandAsync(results, ArtifactCatalog.DocumentId, () => {
            var built = ArtifactCatalogBuilder.BuildFromJson(afx, GameVersionForAfx());
            string? note = built.Skipped.Count > 0 ? $"{built.Skipped.Count} rows skipped" : null;
            return (ArtifactCatalogBuilder.Serialize(built.File), built.File.Artifacts.Count, note);
        }, ct);
    }

    private async Task LandMissionShipsAsync(List<RebuildDocResult> results, CancellationToken ct) {
        string afx = await services.GetRequiredService<EndpointStore>()
                         .CurrentJsonAsync(DataCatalog.AfxConfigRoute, ct)
                     ?? throw new InvalidOperationException("no captured ei_afx/config to decode");
        await LandAsync(results, MissionShipCatalog.DocumentId, () => {
            var doc = GameDataDocBuilders.BuildMissionShipsFromJson(afx, GameVersionForAfx());
            return (doc.Json, doc.Count, null);
        }, ct);
    }

    private string GameVersionForAfx() =>
        _binaryVersion.Length > 0
            ? _binaryVersion
            : gameDataStore.Provider?.Colleggtibles.GameVersion ?? "";

    private EggIncognitoDbContext Db() =>
        dbContext ?? throw new InvalidOperationException("no database configured");

    private async Task<string> BinaryInputShaAsync(CancellationToken ct) {
        try {
            var rows = await Db().StoredBinaries.AsNoTracking()
                .OrderBy(b => b.Platform).ThenBy(b => b.AppVersion)
                .Select(b => b.Platform + "|" + b.AppVersion + "|" + b.Sha256)
                .ToListAsync(ct);
            return rows.Count == 0 ? "" : Hashes.Sha256Hex(string.Join('\n', rows));
        } catch {
            return "";
        }
    }

    private async Task<IReadOnlyDictionary<string, string?>> StoredInputShasAsync(CancellationToken ct) {
        try {
            return await GameDataStore.InputShasAsync(Db(), ct);
        } catch {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }
    }

    private void LogUnbuilt(IEnumerable<RebuildDocResult> results) {
        foreach (var r in results) {
            if (r.Status is "skipped" or "failed")
                logger.LogWarning("gamedata rebuild {Id} {Status}: {Note}", r.Id, r.Status, r.Note);
        }
    }

    private static string? SkipNote(IReadOnlyList<string> skipped, string kind) =>
        skipped.Count == 0 ? null : $"{skipped.Count} {kind} rows dropped: {string.Join(", ", skipped)}";

    private async Task LandBestAsync(List<RebuildDocResult> results, string id, IReadOnlyList<Candidate> candidates,
        Func<Candidate, (string Json, int Count, string? Note)?> build, CancellationToken ct) {
        string? lastNote = null;
        foreach (var c in candidates) {
            string json;
            int count;
            string? note;
            try {
                var built = build(c);
                if (built is null) {
                    lastNote = $"no extraction from {c.Platform} {c.Version}";
                    continue;
                }

                (json, count, note) = built.Value;
                GameDataProvider.Validate(id, json);
            } catch (Exception ex) {
                lastNote = $"{c.Platform} {c.Version}: {ex.Message}";
                continue;
            }

            try {
                await UpsertAsync(id, json, ct);
            } catch (Exception ex) {
                results.Add(new RebuildDocResult(id, "failed", count, null, "db write failed: " + ex.Message));
                return;
            }

            if (_binaryVersion.Length == 0) {
                _binaryVersion = c.Version;
                _platform = c.Platform;
            }

            string tag = $"{c.Platform} {c.Version}";
            results.Add(new RebuildDocResult(id, "built", count, Encoding.UTF8.GetByteCount(json),
                note is null ? tag : $"{tag}; {note}"));
            return;
        }

        results.Add(new RebuildDocResult(id, "skipped", null, null, lastNote ?? "no candidate binary"));
    }

    private async Task LandAsync(List<RebuildDocResult> results, string id,
        Func<(string Json, int Count, string? Note)> build, CancellationToken ct) {
        string json;
        int count;
        string? note;
        try {
            (json, count, note) = build();
            GameDataProvider.Validate(id, json);
        } catch (Exception ex) {
            results.Add(new RebuildDocResult(id, "failed", null, null, ex.Message));
            return;
        }

        try {
            await UpsertAsync(id, json, ct);
        } catch (Exception ex) {
            results.Add(new RebuildDocResult(id, "failed", count, null, "db write failed: " + ex.Message));
            return;
        }

        results.Add(new RebuildDocResult(id, "built", count, Encoding.UTF8.GetByteCount(json), note));
    }

    private async Task UpsertAsync(string id, string json, CancellationToken ct) {
        string? inputSha = BinaryDocIds.Contains(id, StringComparer.Ordinal) ? _inputSha : null;
        var (changed, previousJson) = await GameDataStore.UpsertAsync(Db(), id, json, inputSha, time.GetUtcNow(), ct);
        if (changed) {
            _prevBinaryVersion ??= DocVersions.BinaryVersion(previousJson);
            _changedDocs.Add(id);
        }
    }
}
