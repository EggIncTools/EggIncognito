using EggIncognito.Core.Services.Farm;
using Ei;
using AssetType = Ei.ShellSpec.Types.AssetType;
using FarmElement = Ei.ShellDB.Types.FarmElement;

namespace EggIncognito.Tests.Farm;

public class FarmAssetCatalogTests {
    private static readonly Lazy<ConfigResponse> ConfigFixture = new(LoadConfig);
    private static readonly Lazy<ShellShowcase> ShowcaseFixture = new(LoadShowcase);

    private static ConfigResponse LoadConfig() =>
        ConfigResponse.Parser.ParseJson(File.ReadAllText(Fixture("get_config.json")));

    private static ShellShowcase LoadShowcase() =>
        ShellShowcase.Parser.ParseJson(File.ReadAllText(Fixture("get_shell_showcase.json")));

    private static string Fixture(string name) =>
        Path.Combine(CaptureSessionManagerTests.RealContentRoot(), "Endpoints", "default", "ei", name);

    private static FarmAssetCatalog Catalog() => FarmAssetCatalog.From(ConfigFixture.Value);

    private static IEnumerable<ShellDB.Types.ShellConfiguration> ShowcaseConfigs() {
        var showcase = ShowcaseFixture.Value;
        foreach (var listing in showcase.Top.Concat(showcase.Featured).Concat(showcase.Fresh)) {
            if (listing.FarmConfig is null) continue;
            foreach (var config in listing.FarmConfig.ShellConfigs) yield return config;
        }
    }

    [Fact]
    public void EveryKnownAssetType_ResolvesToExactlyOneBaseStem() {
        var catalog = Catalog();
        Assert.Equal(97, catalog.KnownAssetTypes.Count);
        Assert.Equal(catalog.KnownAssetTypes.Count, catalog.KnownStems.Count);
        Assert.Equal(catalog.KnownAssetTypes.Count, catalog.BaseStems.Count);
        Assert.All(catalog.KnownAssetTypes, type => {
            string? stem = catalog.BaseStem(type);
            Assert.False(string.IsNullOrEmpty(stem));
            var resolved = catalog.AssetTypeForStem(stem);
            Assert.NotNull(resolved);
            Assert.Equal(type, resolved.Value);
        });
    }

    [Theory]
    [InlineData(AssetType.Coop, "ei_hab_coop")]
    [InlineData(AssetType.ChickenUniverse, "ei_hab_chicken_universe")]
    [InlineData(AssetType.Silo1Large, "ei_silo_1_large")]
    [InlineData(AssetType.Depot7, "ei_depot_7")]
    [InlineData(AssetType.Lab6, "ei_lab_6")]
    [InlineData(AssetType.HatcheryDarkMatter, "ei_hatchery_darkmatter")]
    [InlineData(AssetType.Mailbox, "ei_mailbox_empty")]
    [InlineData(AssetType.Ground, "ei_farm")]
    [InlineData(AssetType.Hardscape, "ei_farm_hardscape")]
    [InlineData(AssetType.Hyperloop, "ei_hyperloop_stop")]
    public void BaseStem_MatchesTheDerivedSamples(AssetType type, string expected) =>
        Assert.Equal(expected, Catalog().BaseStem(type));

    [Fact]
    public void Hangar_TakesTheMajorityStem_NotTheGameSideTypo() {
        var catalog = Catalog();
        Assert.Equal("ei_hab_hangar", catalog.BaseStem(AssetType.Hangar));
        Assert.False(catalog.IsKnownStem("ei_hab_hanger"));
    }

    [Fact]
    public void Silo1Large_Resolves() {
        var catalog = Catalog();
        var piece = Assert.Single(catalog.Resolve(AssetType.Silo1Large));
        Assert.Equal("ei_silo_1_large", piece.Stem);
        Assert.Equal("ei_silo_1_large", piece.BaseStem);
        Assert.Empty(catalog.SubPieceTypes(AssetType.Silo1Large));
        Assert.NotEmpty(catalog.ShellsFor(AssetType.Silo1Large));
    }

    [Fact]
    public void CompositeHatcheries_ResolveEveryPiece() {
        var catalog = Catalog();

        Assert.Equal(
            new[] {
                "ei_hatchery_ai", "ei_hatchery_ai_top_1", "ei_hatchery_ai_top_2", "ei_hatchery_ai_top_3",
                "ei_hatchery_ai_top_4"
            },
            catalog.ResolveStems(AssetType.HatcheryAi));

        Assert.Equal(
            new[] {
                "ei_hatchery_darkmatter", "ei_hatchery_darkmatter_ring_1", "ei_hatchery_darkmatter_ring_2",
                "ei_hatchery_darkmatter_ring_3"
            },
            catalog.ResolveStems(AssetType.HatcheryDarkMatter));

        Assert.Equal(
            new[] { "ei_hatchery_nebula", "ei_hatchery_nebula_middle", "ei_hatchery_nebula_top" },
            catalog.ResolveStems(AssetType.HatcheryNebula));

        Assert.Equal(2, catalog.ResolveStems(AssetType.HatcheryGraviton).Count);
        Assert.Equal(2, catalog.ResolveStems(AssetType.HatcheryUniverse).Count);
        Assert.Equal(2, catalog.ResolveStems(AssetType.HatcheryEnlightenment).Count);
        Assert.Equal(2, catalog.ResolveStems(AssetType.Mailbox).Count);
        Assert.Equal(2, catalog.ResolveStems(AssetType.Hyperloop).Count);
    }

    [Fact]
    public void CompositeShell_ResolvesToItsOwnPieces_WithCdnUrls() {
        var catalog = Catalog();
        var shell = catalog.ShellsFor(AssetType.HatcheryAi)
            .First(s => s.PrimaryAssetType == AssetType.HatcheryAi && s.SetIdentifier == "black_white");

        var pieces = catalog.Resolve(AssetType.HatcheryAi, shell.Identifier);
        Assert.Equal(5, pieces.Count);
        Assert.All(pieces, p => {
            Assert.Equal(shell.Identifier, p.ShellIdentifier);
            Assert.True(p.IsShell);
            Assert.StartsWith("https://", p.Url, StringComparison.Ordinal);
            Assert.False(string.IsNullOrEmpty(p.Checksum));
        });

        Assert.Equal("ei_hatchery_ai_black_white", pieces[0].Stem);
        Assert.Equal("ei_hatchery_ai", pieces[0].BaseStem);
        Assert.Equal(AssetType.HatcheryAiTop1, pieces[1].AssetType);
        Assert.Equal("ei_hatchery_ai_top_1", pieces[1].BaseStem);

        var slot = Assert.Single(catalog.ResolveSlot(AssetType.HatcheryAi, shell.Identifier));
        Assert.Equal(pieces[0], slot);
        Assert.Equal(AssetType.HatcheryAi, catalog.ShellById(shell.Identifier)?.PrimaryAssetType);
        Assert.Null(catalog.ShellById("no_such_shell"));
    }

    [Fact]
    public void SubPieceSlot_ResolvesOnlyItsOwnPiece() {
        var catalog = Catalog();
        var shell = catalog.ShellsFor(AssetType.HatcheryDarkMatter)
            .First(s => s.PrimaryAssetType == AssetType.HatcheryDarkMatter && s.SetIdentifier == "black_white");

        var piece = Assert.Single(catalog.Resolve(AssetType.HatcheryDarkMatterRing2, shell.Identifier));
        Assert.Equal(AssetType.HatcheryDarkMatterRing2, piece.AssetType);
        Assert.Equal("ei_hatchery_darkmatter_ring_2", piece.BaseStem);
        Assert.Contains("ring_2", piece.Stem, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownShellIdentifier_FallsBackToTheDeviceBaseStem() {
        var catalog = Catalog();
        var piece = Assert.Single(catalog.Resolve(AssetType.Lab6, "no_such_shell"));
        Assert.Equal("ei_lab_6", piece.Stem);
        Assert.Equal("ei_lab_6", piece.BaseStem);
        Assert.Null(piece.ShellIdentifier);
        Assert.Null(piece.Url);
        Assert.False(piece.IsShell);
    }

    [Fact]
    public void ShellsFor_FiltersByAssetType_AndCoversSubPieces() {
        var catalog = Catalog();

        var depot7 = catalog.ShellsFor(AssetType.Depot7);
        Assert.Equal(56, depot7.Count);
        Assert.All(depot7, s => Assert.Equal(AssetType.Depot7, s.PrimaryAssetType));

        var rings = catalog.ShellsFor(AssetType.HatcheryDarkMatterRing1);
        Assert.NotEmpty(rings);
        Assert.All(rings, s => Assert.Equal(AssetType.HatcheryDarkMatter, s.PrimaryAssetType));

        Assert.NotEmpty(catalog.ShellsFor(AssetType.Chicken));
        Assert.All(catalog.ShellsFor(AssetType.Chicken), s => Assert.True(s.IsObject));
        Assert.Empty(catalog.ShellsFor(AssetType.Unknown));
    }

    [Fact]
    public void DefaultAppearance_IsAbsentFromTheCommittedCatalog() {
        var catalog = Catalog();
        Assert.Empty(catalog.DefaultShells);
        Assert.Empty(catalog.DefaultShellsFor(AssetType.Coop));
        Assert.All(catalog.KnownAssetTypes, type => Assert.Null(catalog.DefaultShellFor(type)));
    }

    [Fact]
    public void HatcheryCustom_IsTheOneGenuinelyAmbiguousBaseStem() {
        var catalog = Catalog();
        Assert.Equal("ei_hatchery_custom_ce3dprinter", catalog.BaseStem(AssetType.HatcheryCustom));
        Assert.Equal(12, catalog.ShellsFor(AssetType.HatcheryCustom).Count);
        Assert.All(catalog.ShellsFor(AssetType.HatcheryCustom), s => Assert.Null(s.SetIdentifier));
    }

    [Fact]
    public void IsKnownStem_GatesOnDerivedBaseStems() {
        var catalog = Catalog();
        Assert.True(catalog.IsKnownStem("ei_farm"));
        Assert.True(catalog.IsKnownStem("ei_silo_1_large"));
        Assert.True(catalog.IsKnownStem("ei_hatchery_darkmatter_ring_3"));
        Assert.False(catalog.IsKnownStem("ei_ship_rooster"));
        Assert.False(catalog.IsKnownStem(""));
        Assert.False(catalog.IsKnownStem(null));
    }

    [Fact]
    public void ElementOf_GroupsTheSlotVocabulary() {
        Assert.Equal(FarmElement.HenHouse, FarmAssetCatalog.ElementOf(AssetType.Coop));
        Assert.Equal(FarmElement.HenHouse, FarmAssetCatalog.ElementOf(AssetType.ChickenUniverse));
        Assert.Equal(FarmElement.Silo, FarmAssetCatalog.ElementOf(AssetType.Silo1Large));
        Assert.Equal(FarmElement.Mailbox, FarmAssetCatalog.ElementOf(AssetType.MailboxFull));
        Assert.Equal(FarmElement.Hyperloop, FarmAssetCatalog.ElementOf(AssetType.HyperloopTrack));
        Assert.Equal(FarmElement.Hatchery, FarmAssetCatalog.ElementOf(AssetType.HatcheryKindnessExtra));
        Assert.Equal(FarmElement.Hatchery, FarmAssetCatalog.ElementOf(AssetType.HatcheryGravitonTop));
        Assert.Equal(FarmElement.FuelTank, FarmAssetCatalog.ElementOf(AssetType.FuelTank4));
        Assert.Equal(FarmElement.Chicken, FarmAssetCatalog.ElementOf(AssetType.Chicken));
        Assert.Equal(FarmElement.Unknown, FarmAssetCatalog.ElementOf(AssetType.Unknown));
    }

    [Fact]
    public void AssetTypesForElement_CoversTheWholeEnum() {
        Assert.Equal(19, FarmAssetCatalog.AssetTypesForElement(FarmElement.HenHouse).Count);
        Assert.Equal(7, FarmAssetCatalog.AssetTypesForElement(FarmElement.Silo).Count);
        Assert.Equal(2, FarmAssetCatalog.AssetTypesForElement(FarmElement.Mailbox).Count);
        Assert.Equal(2, FarmAssetCatalog.AssetTypesForElement(FarmElement.Hyperloop).Count);
        Assert.Equal(7, FarmAssetCatalog.AssetTypesForElement(FarmElement.Depot).Count);
        Assert.Equal(6, FarmAssetCatalog.AssetTypesForElement(FarmElement.Lab).Count);
        Assert.Equal(48, FarmAssetCatalog.AssetTypesForElement(FarmElement.Hatchery).Count);
        Assert.Equal(3, FarmAssetCatalog.AssetTypesForElement(FarmElement.Hoa).Count);
        Assert.Equal(3, FarmAssetCatalog.AssetTypesForElement(FarmElement.MissionControl).Count);
        Assert.Equal(4, FarmAssetCatalog.AssetTypesForElement(FarmElement.FuelTank).Count);
        Assert.Empty(FarmAssetCatalog.AssetTypesForElement(FarmElement.Unknown));
        Assert.Equal(AssetType.Coop, FarmAssetCatalog.AssetTypesForElement(FarmElement.HenHouse)[0]);
        Assert.Contains(AssetType.HatcheryDarkMatterRing3, FarmAssetCatalog.AssetTypesForElement(FarmElement.Hatchery));
    }

    [Fact]
    public void SlotsWithoutABaseStem_AreOnlyTheOnesTheCatalogNeverShips() {
        var catalog = Catalog();
        var missing = new List<AssetType>();
        foreach (var element in Enum.GetValues<FarmElement>()) {
            if (element is FarmElement.Unknown or FarmElement.Chicken or FarmElement.Hat) continue;
            foreach (var type in FarmAssetCatalog.AssetTypesForElement(element)) {
                if (catalog.BaseStem(type) is null) missing.Add(type);
            }
        }

        Assert.Equal(
            new[] {
                AssetType.SiloAll, AssetType.HatcheryChocolate, AssetType.HatcheryEaster,
                AssetType.HatcheryWaterballoon, AssetType.HatcheryFirework, AssetType.HatcheryPumpkin,
                AssetType.HatcheryUniverseBolt
            },
            missing.OrderBy(static t => (int)t));
    }

    [Fact]
    public void EveryShowcaseShellConfig_ResolvesToAtLeastOneStem() {
        var catalog = Catalog();
        var unresolved = new List<string>();
        int viaShell = 0;
        int viaBaseStem = 0;

        foreach (var config in ShowcaseConfigs()) {
            var pieces = catalog.Resolve(config.AssetType, config.ShellIdentifier);
            if (pieces.Count == 0) {
                unresolved.Add($"{config.AssetType}/{config.ShellIdentifier}");
                continue;
            }

            if (pieces[0].IsShell) viaShell++;
            else viaBaseStem++;
        }

        Assert.Empty(unresolved);
        Assert.Equal(2200, viaShell);
        Assert.Equal(3, viaBaseStem);
    }

    [Fact]
    public void EveryShowcaseAssetType_IsAKnownSlot() {
        var catalog = Catalog();
        foreach (var config in ShowcaseConfigs()) {
            Assert.NotEqual(FarmElement.Unknown, FarmAssetCatalog.ElementOf(config.AssetType));
            Assert.False(string.IsNullOrEmpty(catalog.BaseStem(config.AssetType)));
        }
    }

    [Fact]
    public void From_CachesPerCatalogInstance() {
        var config = ConfigFixture.Value;
        Assert.Same(FarmAssetCatalog.From(config.DlcCatalog), FarmAssetCatalog.From(config.DlcCatalog));
        Assert.Same(FarmAssetCatalog.From(config), FarmAssetCatalog.From(config.DlcCatalog));
        Assert.Same(FarmAssetCatalog.Empty, FarmAssetCatalog.From((DLCCatalog?)null));
    }

    [Fact]
    public void Empty_ResolvesNothing() {
        Assert.Empty(FarmAssetCatalog.Empty.KnownAssetTypes);
        Assert.Empty(FarmAssetCatalog.Empty.Resolve(AssetType.Coop));
        Assert.Null(FarmAssetCatalog.Empty.BaseStem(AssetType.Coop));
    }
}
