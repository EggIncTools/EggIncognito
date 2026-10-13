using System.Collections.Frozen;
using System.Text.Json;
using EggIncognito.Core.Services.ProtoExtract;
using EggSharp.GameData.Farm;

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

    private static readonly FrozenDictionary<string, string> ResearchDimensionTargets = new Dictionary<string, string>(StringComparer.Ordinal) {
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
        ["vehicleRoundTripTimeMult"] = "VehicleRoundTripTime",
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
    }.ToFrozenDictionary();

    public static DocResult BuildResearch(IReadOnlyList<ResearchCatalogExtractor.ResearchEntry> entries,
        string binaryVersion, IReadOnlyDictionary<string, ResearchPriceExtractor.Price>? prices = null) {
        var rows = new List<object>();
        var skipped = new List<string>();
        foreach (var e in entries) {
            if (e.Parts.Count == 0) {
                skipped.Add($"{e.Id} ({e.DecodeNote ?? "undecoded"})");
                continue;
            }

            string? unmapped = e.Parts.Select(p => p.Dimension)
                .FirstOrDefault(d => !ResearchDimensionTargets.ContainsKey(d));
            if (unmapped is not null) {
                skipped.Add($"{e.Id} (unmapped dimension {unmapped})");
                continue;
            }

            for (int k = 0; k < e.Parts.Count; k++) {
                var p = e.Parts[k];
                var meta = new Dictionary<string, object>(StringComparer.Ordinal) { ["epic"] = e.Epic };
                if (e.Name is not null) meta["name"] = e.Name;
                if (e.Description is not null) meta["description"] = e.Description;
                if (e.Help is not null) meta["help"] = e.Help;
                meta["dimension"] = p.Dimension;
                if (e.Tier is not null) meta["tier"] = e.Tier.Value;
                if (e.Parts.Count > 1) meta["research"] = e.Id;
                if (prices is not null && prices.TryGetValue(e.Id, out var price)) {
                    if (price.Epic) {
                        meta["epicPriceStart"] = price.EpicPriceStart;
                        meta["epicPriceEnd"] = price.EpicPriceEnd;
                    } else {
                        meta["priceStart"] = price.PriceStart;
                        meta["priceEnd"] = price.PriceEnd;
                        meta["pricePercent"] = price.PricePercent;
                    }
                }

                rows.Add(new {
                    id = e.Parts.Count > 1 ? $"{e.Id}:{p.Dimension}" : e.Id,
                    target = ResearchDimensionTargets[p.Dimension],
                    combineMode = p.CombineMode.ToString(),
                    magnitude = p.Magnitude,
                    maxLevel = e.MaxLevel,
                    meta
                });
            }
        }

        var provenance = new Dictionary<string, BoostCatalogBuilder.ProvenanceSource>(StringComparer.Ordinal) {
            ["identity"] = new("binary", "researchdata"),
            ["maxLevel"] = new("binary", "researchdata", "decoded"),
            ["effects"] = new("binary", "researchdata", "decoded"),
            ["epic"] = new("binary", "researchdata", "decoded")
        };
        if (prices is not null) provenance["price"] = new("binary", "researchdata", "decoded");
        var doc = new { rows, binaryVersion, provenance };
        return new DocResult(JsonSerializer.Serialize(doc, CamelJson), rows.Count, skipped);
    }

    private static readonly FrozenDictionary<int, string> GameDimensionTargets = new Dictionary<int, string> {
        [1] = "UnlimitedHatchery",
        [2] = "Earnings",
        [3] = "AwayEarnings",
        [4] = "IHR",
        [5] = "EggValue",
        [6] = "EggLayingRate",
        [7] = "BoostEffectiveness",
        [8] = "BoostDuration",
        [9] = "SoulEggCollectionRate",
        [10] = "Cash",
        [11] = "SoulMirror",
        [12] = "SoulEggBonus",
        [13] = "DroneRewards",
        [14] = "DroneFrequency",
        [15] = "GiftRewards",
        [16] = "GoldGiftChance",
        [17] = "CurrencyGiftChance",
        [18] = "ResearchCost",
        [19] = "VehicleCapacity",
        [20] = "HoldToHatchRate",
        [21] = "FarmValue",
        [22] = "CoopEarnings",
        [23] = "CoopEggLaying",
        [24] = "RunningChickenBonusCap",
        [25] = "HabCapacity",
        [26] = "ProphecyEggBonus",
        [27] = "SoulEggs",
        [29] = "EnlightenmentEffect"
    }.ToFrozenDictionary();

    private const int PercentDimension = 12;
    private const int CountDimension = 20;

    private static string CombineModeFor(int dimension) =>
        dimension is PercentDimension or CountDimension ? "Add" : "Mul";

    public static DocResult BuildBoosts(IReadOnlyList<BoostEffectExtractor.BoostEffectEntry> entries,
        string binaryVersion) {
        var rows = new List<object>();
        var skipped = new List<string>();
        foreach (var e in entries) {
            if (!GameDimensionTargets.TryGetValue(e.Dimension, out string? target)) {
                skipped.Add($"{e.Id} (unmapped dimension {e.Dimension})");
                continue;
            }

            var meta = new Dictionary<string, object>(StringComparer.Ordinal) {
                ["kind"] = target,
                ["durationSeconds"] = (long)e.DurationSeconds,
                ["multiplier"] = e.Magnitude
            };
            rows.Add(new {
                id = e.Id,
                target,
                combineMode = CombineModeFor(e.Dimension),
                magnitude = e.Magnitude,
                meta
            });
        }

        var doc = new {
            rows,
            binaryVersion,
            provenance = new Dictionary<string, BoostCatalogBuilder.ProvenanceSource>(StringComparer.Ordinal) {
                ["identity"] = new("binary", "boostmanager"),
                ["effects"] = new("binary", "boostmanager", "decoded"),
                ["target"] = new("binary", "GameDimensions::name_str", "manual")
            }
        };
        return new DocResult(JsonSerializer.Serialize(doc, CamelJson), rows.Count, skipped);
    }

    public static DocResult BuildArtifacts(IReadOnlyList<ArtifactTableExtractor.Family> families,
        string binaryVersion) {
        var rows = new List<object>();
        var skipped = new List<string>();
        foreach (var f in families.OrderBy(f => f.Order)) {
            if ((Ei.ArtifactSpec.Types.Type)f.Kind is Ei.ArtifactSpec.Types.Type.Ingredient
                or Ei.ArtifactSpec.Types.Type.StoneIngredient) continue;
            if (!GameDimensionTargets.TryGetValue(f.Dimension, out string? target)) {
                skipped.Add($"{f.BinaryId} (unmapped dimension {f.Dimension})");
                continue;
            }

            foreach (var t in f.Tiers.OrderBy(t => t.Level)) {
                foreach (var e in t.Effects) {
                    rows.Add(new {
                        id = $"{f.BinaryId}_t{t.Level}_r{e.Rarity}",
                        target,
                        combineMode = CombineModeFor(f.Dimension),
                        magnitude = e.Magnitude,
                        meta = new Dictionary<string, object>(StringComparer.Ordinal) {
                            ["boost"] = f.BinaryId,
                            ["tier"] = t.Level,
                            ["rarity"] = e.Rarity,
                            ["stoneCap"] = e.Slots
                        }
                    });
                }
            }
        }

        var doc = new {
            rows,
            binaryVersion,
            provenance = new Dictionary<string, BoostCatalogBuilder.ProvenanceSource>(StringComparer.Ordinal) {
                ["identity"] = new("binary", "artifactdata", "decoded"),
                ["effects"] = new("binary", "artifactdata", "decoded"),
                ["stoneCap"] = new("binary", "artifactdata RarityData+0x0 via ArtifactsManager::attemptSetStones", "decoded"),
                ["target"] = new("binary", "GameDimensions::name_str", "manual")
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

    public static DocResult BuildMissionShips(Ei.ArtifactsConfigurationResponse config, string binaryVersion) {
        var shipEnum = Ei.ArtifactsConfigurationResponse.Types.MissionParameters.Descriptor
            .FindFieldByNumber(Ei.ArtifactsConfigurationResponse.Types.MissionParameters.ShipFieldNumber).EnumType;
        var durationEnum = Ei.ArtifactsConfigurationResponse.Types.MissionParameters.Types.Duration.Descriptor
            .FindFieldByNumber(Ei.ArtifactsConfigurationResponse.Types.MissionParameters.Types.Duration
                .DurationTypeFieldNumber).EnumType;
        var ships = config.MissionParameters.Select(p => new {
            ship = shipEnum.FindValueByNumber((int)p.Ship)?.Name ?? p.Ship.ToString(),
            levelMissionRequirements = p.LevelMissionRequirements.Count == 0 ? null : p.LevelMissionRequirements.ToArray(),
            durations = p.Durations.Select(d => new {
                type = durationEnum.FindValueByNumber((int)d.DurationType)?.Name ?? d.DurationType.ToString(),
                seconds = d.Seconds,
                capacity = d.Capacity,
                levelCapacityBump = d.LevelCapacityBump,
                quality = d.Quality,
                minQuality = d.MinQuality,
                maxQuality = d.MaxQuality,
                levelQualityBump = d.LevelQualityBump
            }).ToArray()
        }).ToArray();
        var doc = new {
            ships,
            binaryVersion,
            provenance = new Dictionary<string, BoostCatalogBuilder.ProvenanceSource>(StringComparer.Ordinal) {
                ["ships"] = new("fixture", "ei_afx/config", "captured")
            }
        };
        return new DocResult(JsonSerializer.Serialize(doc, CamelJson), ships.Sum(s => s.durations.Length), []);
    }

    public static DocResult BuildMissionShipsFromJson(string afxConfigJson, string binaryVersion) =>
        BuildMissionShips(Google.Protobuf.JsonParser.Default.Parse<Ei.ArtifactsConfigurationResponse>(afxConfigJson),
            binaryVersion);

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
