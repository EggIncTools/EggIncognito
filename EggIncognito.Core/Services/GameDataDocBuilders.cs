using System.Text.Json;
using EggIncognito.Core.Services.Farm;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Core.Services;

public static class GameDataDocBuilders {
    private static readonly JsonSerializerOptions CamelJson = JsonPresets.CamelIndentedRelaxed;

    public sealed record DocResult(string Json, int Count, IReadOnlyList<string> Skipped);

    public static DocResult BuildMissions(IReadOnlyList<MissionCatalogExtractor.MissionEntry> entries,
        string binaryVersion) {
        var skipped = entries.Where(e => string.IsNullOrEmpty(e.Goal)).Select(e => e.Id).ToList();
        var rows = entries.Where(e => !string.IsNullOrEmpty(e.Goal))
            .Select(e => new { id = e.Id, displayName = e.DisplayName, goal = e.Goal })
            .ToArray();
        var doc = new {
            missions = rows,
            binaryVersion,
            provenance = new Dictionary<string, BoostCatalogBuilder.ProvenanceSource>(StringComparer.Ordinal) {
                ["identity"] = new("binary", "missiondata"),
                ["goal"] = new("binary", "missiondata", "decoded")
            }
        };
        return new DocResult(JsonSerializer.Serialize(doc, CamelJson), rows.Length, skipped);
    }

    public static DocResult BuildEggs(IReadOnlyList<EggCatalogExtractor.EggEntry> entries, string binaryVersion) {
        var rows = entries.Select(e => new { index = e.Index, name = e.Name, baseValue = e.BaseValue }).ToArray();
        var doc = new {
            eggs = rows,
            binaryVersion,
            provenance = new Dictionary<string, BoostCatalogBuilder.ProvenanceSource>(StringComparer.Ordinal) {
                ["identity"] = new("binary", "eggdata"),
                ["baseValue"] = new("binary", "eggdata", "decoded")
            }
        };
        return new DocResult(JsonSerializer.Serialize(doc, CamelJson), rows.Length, []);
    }

    public static DocResult BuildVehicles(IReadOnlyList<VehicleCatalogExtractor.VehicleEntry> entries,
        string binaryVersion) {
        var skipped = entries.Where(e => string.IsNullOrEmpty(e.Name))
            .Select(e => e.Index.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        var rows = entries.Where(e => !string.IsNullOrEmpty(e.Name))
            .Select(e => new { index = e.Index, name = e.Name, capacity = e.Capacity })
            .ToArray();
        var doc = new {
            vehicles = rows,
            binaryVersion,
            provenance = new Dictionary<string, BoostCatalogBuilder.ProvenanceSource>(StringComparer.Ordinal) {
                ["identity"] = new("binary", "vehicledata"),
                ["capacity"] = new("binary", "vehicledata", "decoded")
            }
        };
        return new DocResult(JsonSerializer.Serialize(doc, CamelJson), rows.Length, skipped);
    }

    private static readonly Dictionary<string, string> ResearchDimensionTargets = new(StringComparer.Ordinal) {
        ["eggLayingRateMult"] = "EggLayingRate",
        ["eggValueMult"] = "EggValue",
        ["earningsMult"] = "Earnings",
        ["earningsMultAway"] = "AwayEarnings",
        ["coopEarningsMult"] = "CoopEarnings",
        ["coopEggLayingRateMult"] = "CoopEggLaying",
        ["habCapacityMult"] = "HabCapacity",
        ["portalCapacityMult"] = "PortalHabCapacity",
        ["hatcheryCapacity"] = "HatcheryCapacity",
        ["hatcheryRefillRateMult"] = "HatcheryRefillRate",
        ["onscreenChickenMult"] = "RunningChickenBonus",
        ["onscreenChickenMultMaxBase"] = "RunningChickenBonusCap",
        ["onscreenChickenMultMult"] = "RunningChickenBonusMult",
        ["internalHatcheryRateBase"] = "IHRBase",
        ["internalHatcherySharing"] = "IHRSharing",
        ["internalHatcheryMult"] = "IHR",
        ["internalHatcheryMultAway"] = "IHROffline",
        ["vehicleSpeedMult"] = "VehicleSpeed",
        ["vehicleCapacityMult"] = "VehicleCapacity",
        ["vehicleCapacityMultHover"] = "HoverVehicleCapacity",
        ["vehicleLoadingTimeMult"] = "VehicleLoadingTime",
        ["maxFleetSize"] = "FleetSize",
        ["hyperloopCarCapacityMult"] = "HyperloopCarCapacity",
        ["hyperloopMaxTrainLength"] = "HyperloopTrainLength",
        ["siloSeconds"] = "SiloTime",
        ["vehicleCostMult"] = "VehicleCost",
        ["habCostMult"] = "HabCost",
        ["researchCostMult"] = "ResearchCost",
        ["epicResearchCostMult"] = "EpicResearchCost",
        ["boostCostMult"] = "BoostCost",
        ["boostDurationMult"] = "BoostDuration",
        ["boostBoostMult"] = "BoostEffectiveness",
        ["valuationMult"] = "FarmValue",
        ["soulEggBonus"] = "SoulEggBonus",
        ["soulEggCollectionMult"] = "SoulEggCollectionRate",
        ["prestigeEarningsMult"] = "PrestigeEarnings",
        ["eggOfProphecyBonus"] = "ProphecyEggBonus",
        ["droneRewardMult"] = "DroneRewards",
        ["droneRewardQualityMult"] = "DroneRewardQuality",
        ["droneFrequencyMult"] = "DroneFrequency",
        ["giftRewardMult"] = "GiftRewards",
        ["videoDoublerHours"] = "VideoDoublerTime",
        ["holdToHatchRate"] = "HoldToHatchRate",
        ["holdToResearchMult"] = "HoldToResearch",
        ["artifactsMissionCapacityResearchMult"] = "AfxMissionCapacity",
        ["artifactsMissionFTLDurationResearchMult"] = "AfxMissionDuration"
    };

    public static DocResult BuildResearch(IReadOnlyList<ResearchCatalogExtractor.ResearchEntry> entries,
        string binaryVersion) {
        var rows = new List<object>();
        var skipped = new List<string>();
        foreach (var e in entries) {
            if (e.Dimension is null || e.CombineMode is null || e.Magnitude is null ||
                !ResearchDimensionTargets.TryGetValue(e.Dimension, out string? target)) {
                skipped.Add($"{e.Id} ({e.DecodeNote ?? "unmapped dimension " + e.Dimension})");
                continue;
            }

            var meta = new Dictionary<string, object>(StringComparer.Ordinal) { ["epic"] = e.Epic };
            if (e.Name is not null) meta["name"] = e.Name;
            if (e.Description is not null) meta["description"] = e.Description;
            if (e.Help is not null) meta["help"] = e.Help;
            meta["dimension"] = e.Dimension;
            if (e.Tier is not null) meta["tier"] = e.Tier.Value;

            rows.Add(new {
                id = e.Id,
                target,
                combineMode = e.CombineMode.Value.ToString(),
                magnitude = e.Magnitude.Value,
                maxLevel = e.MaxLevel,
                meta
            });
        }

        var doc = new {
            rows,
            binaryVersion,
            provenance = new Dictionary<string, BoostCatalogBuilder.ProvenanceSource>(StringComparer.Ordinal) {
                ["identity"] = new("binary", "researchdata"),
                ["maxLevel"] = new("binary", "researchdata", "decoded"),
                ["effects"] = new("binary", "researchdata", "decoded"),
                ["epic"] = new("binary", "researchdata", "decoded")
            }
        };
        return new DocResult(JsonSerializer.Serialize(doc, CamelJson), rows.Count, skipped);
    }

    public static DocResult BuildHabs(IReadOnlyList<HabCatalogExtractor.HabEntry> entries, string binaryVersion) {
        var skipped = entries.Where(e => string.IsNullOrEmpty(e.Name))
            .Select(e => e.Index.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        var rows = entries.Where(e => !string.IsNullOrEmpty(e.Name))
            .Select(e => new {
                id = HabIdFromName(e.Name!),
                target = "HabCapacity",
                combineMode = "Add",
                magnitude = (double)e.Capacity,
                maxLevel = 1,
                meta = new Dictionary<string, object>(StringComparer.Ordinal) {
                    ["habId"] = e.Index,
                    ["name"] = e.Name!
                }
            })
            .ToArray();
        var doc = new {
            rows,
            binaryVersion,
            provenance = new Dictionary<string, BoostCatalogBuilder.ProvenanceSource>(StringComparer.Ordinal) {
                ["identity"] = new("binary", "habdata"),
                ["capacity"] = new("binary", "habdata", "decoded"),
                ["id"] = new("derived")
            }
        };
        return new DocResult(JsonSerializer.Serialize(doc, CamelJson), rows.Length, skipped);
    }

    public static string HabIdFromName(string name) {
        var sb = new System.Text.StringBuilder(name.Length);
        bool pendingSep = false;
        foreach (char c in name) {
            if (char.IsAsciiLetterOrDigit(c)) {
                if (pendingSep && sb.Length > 0) sb.Append('_');
                pendingSep = false;
                sb.Append(char.ToLowerInvariant(c));
            } else if (c == ' ') {
                pendingSep = true;
            }
        }

        return sb.ToString();
    }

    public static DocResult BuildFarmPlacement(FarmPlacementData data, string binaryVersion) {
        var skipped = data.Habs.Where(h => string.IsNullOrEmpty(h.Name))
            .Select(h => "hab " + h.Index.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Concat(data.Eggs.Where(e => string.IsNullOrEmpty(e.Name))
                .Select(e => "egg " + e.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

        var trophyGeometry = data.Trophy;
        var roadGeometry = data.Road;
        var doc = new {
            habs = data.Habs.Select(h => new {
                index = h.Index,
                name = h.Name,
                width = h.Width,
                extent = h.Extent,
                depth = h.Depth
            }).ToArray(),
            habRow = new { anchorX = data.HabAnchorX, y = data.HabRowY, z = data.HabRowZ, gap = data.HabGap },
            silos = new {
                stepX = data.SiloStepX,
                baseX = data.SiloBaseX,
                y = data.SiloY,
                zEven = data.SiloZEven,
                zOdd = data.SiloZOdd
            },
            trophy = new {
                casePos = Vector(trophyGeometry.CasePos),
                columnStepX = trophyGeometry.ColumnStepX,
                originX = trophyGeometry.OriginX,
                rowStepY = trophyGeometry.RowStepY,
                originY = trophyGeometry.OriginY,
                rowStepZ = trophyGeometry.RowStepZ,
                originZ = trophyGeometry.OriginZ,
                columns = trophyGeometry.Columns,
                count = trophyGeometry.Count,
                bonusScale = trophyGeometry.BonusScale,
                bonusPos = Vector(trophyGeometry.BonusPos)
            },
            labExtents = data.LabExtents,
            depotExtents = data.DepotExtents,
            eggs = data.Eggs.Select(e => new {
                index = e.Index,
                name = e.Name,
                hatcheryExtent = e.HatcheryExtent
            }).ToArray(),
            missionControlPose = data.MissionControlPose.Select(Vector).ToArray(),
            fuelTankSpacing = data.FuelTankSpacing,
            singletons = new {
                floor = data.SingletonFloor,
                hoaHomeOffset = data.HoaHomeOffset,
                hoaAltOffset = data.HoaAltOffset,
                hoaZ = data.HoaZ,
                missionControlOffset = data.MissionControlOffset,
                fuelTankBaseOffset = data.FuelTankBaseOffset,
                fuelTankLockedExtra = data.FuelTankLockedExtra,
                fuelTankZUnlocked = data.FuelTankZUnlocked,
                fuelTankZLocked = data.FuelTankZLocked
            },
            camera = new {
                distance = data.CameraDistance,
                height = data.CameraHeight,
                staticFocus = data.CameraStaticFocus.Select(Vector).ToArray(),
                habFocusOffset = Vector(data.HabFocusOffset),
                labFocusBase = Vector(data.LabFocusBase),
                depotFocusBase = Vector(data.DepotFocusBase),
                hatcheryFocusBase = Vector(data.HatcheryFocusBase),
                fuelTankFocusOffset = Vector(data.FuelTankFocusOffset),
                focusExtentPivot = data.FocusExtentPivot,
                focusExtentScale = data.FocusExtentScale,
                hoaFocusExtra = data.HoaFocusExtra,
                uiDivisor = data.CameraUiDivisor,
                uiHeightScale = data.CameraUiHeightScale,
                uiDistanceScale = data.CameraUiDistanceScale
            },
            vehicles = data.Vehicles.Select(v => new { index = v.Index, name = v.Name, length = v.Length })
                .ToArray(),
            road = new {
                spawnX = roadGeometry.SpawnX,
                roadZ = roadGeometry.RoadZ,
                roadY = roadGeometry.RoadY,
                depotStopX = roadGeometry.DepotStopX,
                despawnX = roadGeometry.DespawnX,
                followGap = roadGeometry.FollowGap,
                maxSpeedMult = roadGeometry.MaxSpeedMult,
                roundTripSeconds = roadGeometry.RoundTripSeconds,
                hyperloopVehicleIndex = roadGeometry.HyperloopVehicleIndex,
                emptyVehicleIndex = roadGeometry.EmptyVehicleIndex
            },
            binaryVersion,
            provenance = data.Provenance.ToDictionary(p => p.Key,
                p => new BoostCatalogBuilder.ProvenanceSource(OriginName(p.Value.Origin), p.Value.Locator,
                    p.Value.Method ?? "decoded"), StringComparer.Ordinal)
        };
        return new DocResult(JsonSerializer.Serialize(doc, CamelJson), data.Habs.Count, skipped);
    }

    private sealed record VectorRow(float X, float Y, float Z);

    private static VectorRow Vector(Vec3 v) => new(v.X, v.Y, v.Z);

    private static string OriginName(PlacementOrigin origin) => origin switch {
        PlacementOrigin.Binary => "binary",
        PlacementOrigin.Config => "config",
        PlacementOrigin.Fixture => "fixture",
        PlacementOrigin.Derived => "derived",
        _ => "authored"
    };

    public static DocResult BuildDimensions(IReadOnlyList<string> ids, string binaryVersion) {
        var doc = new {
            dimensions = ids,
            binaryVersion,
            provenance = new Dictionary<string, BoostCatalogBuilder.ProvenanceSource>(StringComparer.Ordinal) {
                ["identity"] = new("binary", "boostmanager")
            }
        };
        return new DocResult(JsonSerializer.Serialize(doc, CamelJson), ids.Count, []);
    }
}
