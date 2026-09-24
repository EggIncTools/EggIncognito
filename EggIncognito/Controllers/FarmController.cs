using System.Buffers;
using EggIncognito.Core.Services.Farm;
using EggIncognito.Models.Farm;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.DataApi;
using Ei;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using AssetType = Ei.ShellSpec.Types.AssetType;
using FarmElement = Ei.ShellDB.Types.FarmElement;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/farm")]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class FarmController(
    GameConfigStore configStore,
    FarmPlacementDataProvider placementData,
    DeviceMeshProvider deviceMeshes,
    ShipShellDownloader downloader,
    MeshAssetCache cache,
    IServiceProvider services,
    IAppMode appMode,
    ICurrentUser currentUser) : ApiControllerBase {
    private const string SubPieceMethod =
        "sub-piece drawn at the parent transform; hatchery geometry is baked in the rpo";

    private static readonly SearchValues<char> InvalidStemChars = SearchValues.Create("/\\.");

    [HttpGet("catalog")]
    [EnableRateLimiting("read")]
    public IActionResult Catalog([FromQuery] string platform = "ios") {
        var catalog = FarmAssetCatalog.From(LoadCatalog(platform));
        if (catalog.KnownAssetTypes is [])
            return Ok(new {
                ok = false,
                platform,
                diagnostics = $"no stored config for {platform}; ingest one via /api/config"
            });

        var elements = Enum.GetValues<FarmElement>()
            .Where(e => e != FarmElement.Unknown)
            .Select(e => new {
                element = e.ToString(),
                assetTypes = FarmAssetCatalog.AssetTypesForElement(e)
                    .Where(t => catalog.BaseStem(t) is not null)
                    .Select(t => Describe(catalog, t))
            });

        return Ok(new {
            ok = true,
            platform,
            assetTypeCount = catalog.KnownAssetTypes.Count,
            elements,
            unresolvedAssetTypes = Enum.GetValues<AssetType>()
                .Where(t => FarmAssetCatalog.ElementOf(t) != FarmElement.Unknown && catalog.BaseStem(t) is null)
                .Select(t => t.ToString())
        });
    }

    [HttpGet("showcase")]
    [EnableRateLimiting("read")]
    public IActionResult Showcase() {
        var parsed = FarmShowcase.Parse(DataCatalog.FixtureText(services, DataCatalog.ShowcaseRoute));
        return !parsed.Ok
            ? Ok(new { ok = false, diagnostics = parsed.Diagnostics })
            : Ok(new {
                ok = true,
                count = parsed.Presets.Count,
                presets = parsed.Presets.Select(p => new { p.Id, p.Name, p.Bucket })
            });
    }

    [HttpPost("layout")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Layout([FromBody] FarmLayoutRequest? request, CancellationToken ct) {
        var req = request ?? new FarmLayoutRequest();
        string platform = string.IsNullOrWhiteSpace(req.Platform) ? "ios" : req.Platform;

        (var data, string? dataDiag) = await placementData.GetAsync(ct);
        if (data is null) return Ok(new { ok = false, diagnostics = dataDiag });

        (var state, string? stateDiag) = BuildState(req);
        if (state is null) return Ok(new { ok = false, diagnostics = stateDiag });

        var catalog = FarmAssetCatalog.From(LoadCatalog(platform));
        var layout = FarmPlacementEngine.Place(state, data);
        var shells = ShellIndex(state.Appearance);
        var rows = new List<object>();
        var skipped = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var p in layout.Placements) {
            Emit(rows, skipped, seen, catalog, shells, platform, p, p.AssetType, p.Provenance);
            if (p.AssetType is not { } parent) continue;
            if (FarmAssetCatalog.ElementOf(parent) != FarmElement.Hatchery) continue;
            foreach (var sub in catalog.SubPieceTypes(parent)) {
                Emit(rows, skipped, seen, catalog, shells, platform, p, sub,
                    new PlacementProvenance(PlacementOrigin.Derived, p.Provenance.Locator, SubPieceMethod));
            }
        }

        return Ok(new {
            ok = true,
            platform,
            binaryVersion = data.BinaryVersion,
            extents = new {
                layout.Extents.Lab,
                layout.Extents.Depot,
                layout.Extents.Hatchery,
                layout.Extents.HatcheryResolved
            },
            state = Describe(state),
            lighting = LightingOf(state.Appearance),
            placements = rows,
            motion = Motion(state, data, layout, catalog, platform, req.ChickenCount ?? 0),
            unresolved = skipped,
            diagnostics = Diagnose(data, layout.Extents, state)
        });
    }

    [HttpGet("camera")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Camera([FromQuery] string element, [FromQuery] int index = 0,
        [FromQuery] float topUiStart = 0f, CancellationToken ct = default) {
        if (!Enum.TryParse<FarmElement>(element, true, out var parsed) || parsed == FarmElement.Unknown)
            return Fail(400, "unknown farm element");

        (var data, string? diag) = await placementData.GetAsync(ct);
        if (data is null) return Ok(new { ok = false, diagnostics = diag });

        var state = new FarmState();
        var shot = FarmCameraEngine.Compose(FarmCameraEngine.Shot(state, data, parsed, index), topUiStart, data);
        return Ok(new {
            ok = true,
            element = parsed.ToString(),
            index,
            focus = new { shot.Focus.X, shot.Focus.Y, shot.Focus.Z },
            distance = shot.Distance,
            height = shot.Height,
            locator = FarmCameraEngine.InfoLocator
        });
    }

    [HttpGet("mesh/{stem}")]
    [EnableRateLimiting("egress")]
    public async Task<IActionResult> Mesh(string stem, [FromQuery] string platform = "ios",
        [FromQuery] string? shell = null, CancellationToken ct = default) {
        if (appMode.Mode == AppMode.Hosted && !currentUser.IsAuthenticated)
            return Fail(403, "log in to download farm meshes from the hosted site");
        if (string.IsNullOrEmpty(stem) || stem.AsSpan().IndexOfAny(InvalidStemChars) >= 0)
            return Fail(400, "invalid mesh name");

        var catalog = FarmAssetCatalog.From(LoadCatalog(platform));
        string? url = UrlFor(catalog, stem, shell);
        if (url is null) {
            var pulled = await deviceMeshes.GetGlbAsync(stem, null, ct);
            return pulled.Ok
                ? File(pulled.Glb, "model/gltf-binary", $"{stem}.glb")
                : Fail(pulled.Status, pulled.Diagnostics ?? "mesh pull failed");
        }

        string key = $"{platform}_{stem}";
        byte[]? glb = cache.TryGet("shell", key);
        if (glb is null) {
            var decode = await downloader.DownloadAndDecodeAsync(url, stem, ct);
            if (!decode.Ok) return Fail(502, decode.Diagnostics);
            glb = decode.Glb;
            await cache.PutAsync("shell", key, glb, ct);
        }

        return File(glb, "model/gltf-binary", $"{stem}.glb");
    }

    private static string? Diagnose(FarmPlacementData data, FarmExtents extents, FarmState state) {
        var notes = new List<string>();
        if (!data.IsComplete) notes.Add("the farm-placement document is incomplete; some tables are missing");
        if (!extents.HatcheryResolved)
            notes.Add($"no hatchery extent for egg {state.EggType}, so HOA, mission control and the fuel tank "
                      + "fall back to the depot extent alone");

        if (state.HabTiersInferred)
            notes.Add("this appearance carries no shell_configs, so hab tiers are unknown; set them in the farm "
                      + "state panel because hab width drives the row positions");

        if (state.Appearance is { } appearance && FarmStateBuilder.CarriesNoAppearance(appearance))
            notes.Add("this listing carries no appearance at all: no shell, shell-set, group, chicken or lighting "
                      + "configuration, so the farm renders empty with default shells");

        return notes.Count == 0 ? null : string.Join("; ", notes);
    }

    private static object Motion(FarmState state, FarmPlacementData data, FarmLayout layout,
        FarmAssetCatalog catalog, string platform, int chickenCount) {
        var lengths = data.Vehicles.ToDictionary(v => v.Index, v => v.Length);
        var vehicles = state.Vehicles
            .Select((type, slot) => new {
                index = slot,
                type,
                length = lengths.GetValueOrDefault(type, 0d),
                stem = (string?)null,
                meshUrl = (string?)null
            })
            .Where(v => v.type != data.Road.HyperloopVehicleIndex && v.type != data.Road.EmptyVehicleIndex)
            .ToList();

        var habs = layout.Placements
            .Where(p => p.Element == FarmElement.HenHouse && p.AssetType is not null)
            .Select(p => new {
                key = $"{p.Element}:{p.Index}:{p.AssetType}",
                p.Index,
                pos = new { p.Pos.X, p.Pos.Y, p.Pos.Z },
                depth = data.HabDepth((int)p.AssetType!.Value - 1)
            })
            .ToList();

        var chicken = ChickenPiece(state, catalog);
        return new {
            road = new {
                data.Road.SpawnX,
                data.Road.RoadY,
                data.Road.RoadZ,
                data.Road.DepotStopX,
                data.Road.DespawnX,
                data.Road.FollowGap,
                data.Road.MaxSpeedMult,
                data.Road.RoundTripSeconds,
                data.Road.HyperloopVehicleIndex,
                data.Road.EmptyVehicleIndex
            },
            vehicles,
            vehicleMeshDiagnostics = vehicles.Count == 0
                ? null
                : "vehicle meshes are app-bundle rpos, not DLC shells; no stem source is wired yet",
            chickens = new {
                count = Math.Clamp(chickenCount, 0, 200),
                stem = chicken?.Stem,
                meshUrl = chicken is null
                    ? null
                    : $"/api/farm/mesh/{chicken.Stem}?platform={platform}"
                      + (chicken.ShellIdentifier is null ? "" : $"&shell={chicken.ShellIdentifier}"),
                animation = 0,
                habs
            }
        };
    }

    private static FarmMeshPiece? ChickenPiece(FarmState state, FarmAssetCatalog catalog) {
        string? identifier = state.Appearance?.ChickenConfigs
            .Select(c => c.ChickenIdentifier)
            .FirstOrDefault(id => !string.IsNullOrEmpty(id));
        var pieces = catalog.ResolveSlot(AssetType.Chicken, identifier);
        return pieces.Count > 0 ? pieces[0] : null;
    }

    private static void Emit(List<object> rows, List<string> skipped, HashSet<string> seen,
        FarmAssetCatalog catalog, IReadOnlyDictionary<(AssetType, int), string> shells, string platform,
        FarmPlacement placement, AssetType? type, PlacementProvenance provenance) {
        if (type is not { } assetType) {
            if (placement.Stem is null) return;
            Add(rows, seen, placement, placement.Element, null, placement.Stem, null,
                $"/api/farm/mesh/{placement.Stem}?platform={platform}", provenance);
            return;
        }

        string? shellId = shells.GetValueOrDefault((assetType, placement.Index));
        var pieces = catalog.ResolveSlot(assetType, shellId);
        if (pieces.Count == 0) {
            skipped.Add($"{assetType}:{placement.Index}");
            return;
        }

        foreach (var piece in pieces) {
            string url = $"/api/farm/mesh/{piece.Stem}?platform={platform}"
                         + (piece.ShellIdentifier is null ? "" : $"&shell={piece.ShellIdentifier}");
            Add(rows, seen, placement, FarmAssetCatalog.ElementOf(assetType), assetType, piece.Stem,
                piece.ShellIdentifier, url, provenance);
        }
    }

    private static void Add(List<object> rows, HashSet<string> seen, FarmPlacement p, FarmElement element,
        AssetType? assetType, string stem, string? shellIdentifier, string meshUrl,
        PlacementProvenance provenance) {
        string key = assetType is null ? $"{element}:{p.Index}:{stem}" : $"{element}:{p.Index}:{assetType}";
        if (!seen.Add(key)) return;
        rows.Add(Row(key, p, element, assetType, stem, shellIdentifier, meshUrl, provenance));
    }

    private static object Row(string key, FarmPlacement p, FarmElement element, AssetType? assetType, string stem,
        string? shellIdentifier, string meshUrl, PlacementProvenance provenance) => new {
            key,
            element = element.ToString(),
            assetType = assetType?.ToString(),
            p.Index,
            pos = new { p.Pos.X, p.Pos.Y, p.Pos.Z },
            rotDeg = new { p.RotDeg.X, p.RotDeg.Y, p.RotDeg.Z },
            p.Scale,
            stem,
            shellIdentifier,
            meshUrl,
            provenance = new {
                origin = provenance.Origin.ToString(),
                provenance.Locator,
                provenance.Method
            }
        };

    private (FarmState? State, string? Diagnostics) BuildState(FarmLayoutRequest req) {
        if (!string.IsNullOrWhiteSpace(req.ShowcaseId)) {
            var parsed = FarmShowcase.Parse(DataCatalog.FixtureText(services, DataCatalog.ShowcaseRoute));
            if (!parsed.Ok) return (null, parsed.Diagnostics);
            var preset = parsed.Presets.FirstOrDefault(p =>
                string.Equals(p.Id, req.ShowcaseId, StringComparison.Ordinal));
            return preset is null
                ? (null, $"no showcase preset with id {req.ShowcaseId}")
                : (Apply(FarmStateBuilder.FromConfiguration(preset.Config), req.State), null);
        }

        if (req.FarmConfig is { } element) {
            try {
                var config = ShellDB.Types.FarmConfiguration.Parser.ParseJson(element.GetRawText());
                return (Apply(FarmStateBuilder.FromConfiguration(config), req.State), null);
            } catch (InvalidProtocolBufferException ex) {
                return (null, "farmConfig is not a valid ShellDB.FarmConfiguration: " + ex.Message);
            }
        }

        return req.State is null
            ? (null, "supply one of showcaseId, farmConfig or state")
            : (Apply(new FarmState(), req.State), null);
    }

    private static FarmState Apply(FarmState state, FarmStateDto? dto) {
        if (dto is null) return state;
        return state with {
            Habs = dto.Habs is { Count: > 0 } ? dto.Habs : state.Habs,
            SilosOwned = dto.SilosOwned ?? state.SilosOwned,
            LabTier = dto.LabTier ?? state.LabTier,
            DepotTier = dto.DepotTier ?? state.DepotTier,
            HoaTier = dto.HoaTier ?? state.HoaTier,
            MissionControlLevel = dto.MissionControlLevel ?? state.MissionControlLevel,
            FuelTankTier = dto.FuelTankTier ?? state.FuelTankTier,
            HyperloopStation = dto.HyperloopStation ?? state.HyperloopStation,
            HyperloopUnderConstruction = dto.HyperloopUnderConstruction ?? state.HyperloopUnderConstruction,
            ArtifactsEnabled = dto.ArtifactsEnabled ?? state.ArtifactsEnabled,
            HomeFarm = dto.HomeFarm ?? state.HomeFarm,
            FuelTankUnlocked = dto.FuelTankUnlocked ?? state.FuelTankUnlocked,
            HasUnreadMail = dto.HasUnreadMail ?? state.HasUnreadMail,
            AllTrophiesComplete = dto.AllTrophiesComplete ?? state.AllTrophiesComplete,
            EggMedalLevel = dto.EggMedalLevel is { Count: > 0 } ? dto.EggMedalLevel : state.EggMedalLevel,
            EggType = ParseEgg(dto.EggType) ?? state.EggType,
            HatcheryAssetType = ParseEgg(dto.EggType) is { } egg
                ? FarmState.HatcheryFor(egg)
                : state.HatcheryAssetType,
            SiloAssetType = ParseAssetType(dto.SiloAssetType) ?? state.SiloAssetType
        };
    }

    private static Egg? ParseEgg(string? s) =>
        !string.IsNullOrWhiteSpace(s) && Enum.TryParse<Egg>(s, true, out var e) ? e : null;

    private static AssetType? ParseAssetType(string? s) =>
        !string.IsNullOrWhiteSpace(s) && Enum.TryParse<AssetType>(s, true, out var t) ? t : null;

    private static object Describe(FarmState s) => new {
        habs = s.Habs,
        s.HabTiersInferred,
        s.SilosOwned,
        s.SiloCountInferred,
        siloAssetType = s.SiloAssetType.ToString(),
        eggType = s.EggType.ToString(),
        hatcheryAssetType = s.HatcheryAssetType.ToString(),
        s.LabTier,
        s.DepotTier,
        s.HoaTier,
        s.MissionControlLevel,
        s.FuelTankTier,
        s.HyperloopStation,
        s.ArtifactsEnabled,
        s.HomeFarm,
        s.FuelTankUnlocked,
        s.HasUnreadMail
    };

    private static object Describe(FarmAssetCatalog catalog, AssetType type) => new {
        assetType = type.ToString(),
        baseStem = catalog.BaseStem(type),
        subPieces = catalog.SubPieceTypes(type).Select(t => t.ToString()),
        shells = catalog.ShellsFor(type).Select(s => new { s.Identifier, s.Name, s.SetIdentifier })
    };

    private static object? LightingOf(ShellDB.Types.FarmConfiguration? config) {
        if (config?.LightingConfig is not { } l) return null;
        return new {
            enabled = config.LightingConfigEnabled,
            lightDir = Xyz(l.LightDir),
            lightDirectColor = Rgba(l.LightDirectColor),
            l.LightDirectIntensity,
            lightAmbientColor = Rgba(l.LightAmbientColor),
            l.LightAmbientIntensity,
            fogColor = Rgba(l.FogColor),
            l.FogNear,
            l.FogFar,
            l.FogDensity
        };
    }

    private static object? Xyz(Vector3? v) => v is null ? null : new { v.X, v.Y, v.Z };

    private static object? Rgba(Vector4? v) => v is null ? null : new { v.X, v.Y, v.Z, v.W };

    private static Dictionary<(AssetType, int), string> ShellIndex(ShellDB.Types.FarmConfiguration? config) {
        var map = new Dictionary<(AssetType, int), string>();
        if (config is null) return map;
        foreach (var c in config.ShellConfigs) {
            if (string.IsNullOrEmpty(c.ShellIdentifier)) continue;
            map[(c.AssetType, (int)c.Index)] = c.ShellIdentifier;
        }

        return map;
    }

    private static string? UrlFor(FarmAssetCatalog catalog, string stem, string? shell) {
        if (shell is not null) {
            var type = catalog.AssetTypeForStem(stem);
            if (type is { } t) {
                foreach (var piece in catalog.ResolveSlot(t, shell)) {
                    if (string.Equals(piece.Stem, stem, StringComparison.Ordinal) && piece.Url is not null)
                        return piece.Url;
                }
            }

            var reference = catalog.ShellById(shell);
            if (reference is not null) {
                foreach (var piece in catalog.Resolve(reference.PrimaryAssetType, shell)) {
                    if (string.Equals(piece.Stem, stem, StringComparison.Ordinal) && piece.Url is not null)
                        return piece.Url;
                }
            }
        }

        var owner = catalog.AssetTypeForStem(stem);
        if (owner is not { } assetType) return null;
        foreach (var piece in catalog.ResolveSlot(assetType)) {
            if (string.Equals(piece.Stem, stem, StringComparison.Ordinal)) return piece.Url;
        }

        return null;
    }

    private DLCCatalog? LoadCatalog(string platform) {
        var stored = configStore.Get(platform);
        if (stored is null) return null;
        try {
            return ConfigResponse.Parser.ParseJson(stored.Json).DlcCatalog;
        } catch (InvalidProtocolBufferException) {
            return null;
        }
    }
}
