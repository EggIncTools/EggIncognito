using EggIncognito.Core.Services.ProtoExtract;
using Ei;

namespace EggIncognito.Tests.ProtoExtract;

public class ShellCatalogTests {
    [Fact]
    public void FromCatalog_ResolvesPrimaryPieceUrl() {
        var cat = new DLCCatalog();
        var shell = new ShellSpec {
            Identifier = "ei_depot_1_black_white",
            Name = "Black & White",
            ModifiedGeometry = true,
            PrimaryPiece = new ShellSpec.Types.ShellPiece {
                AssetType = ShellSpec.Types.AssetType.Depot1,
                Dlc = new DLCItem {
                    Name = "ei_depot_1_black_white",
                    Directory = "shells",
                    Ext = "rpo",
                    Checksum = "abc",
                    Url = "https://www.auxbrain.com/dlc/shells/ei_depot_1_black_white_hash.rpoz"
                }
            }
        };
        cat.Shells.Add(shell);

        var shells = ShellCatalog.FromCatalog(cat);
        var s = Assert.Single(shells);
        Assert.Equal("ei_depot_1_black_white", s.Identifier);
        Assert.Equal("Depot1", s.AssetType);
        Assert.Equal("https://www.auxbrain.com/dlc/shells/ei_depot_1_black_white_hash.rpoz", s.Url);
        Assert.True(s.ModifiedGeometry);
    }

    [Fact]
    public void ForAssetType_FiltersByType() {
        var cat = new DLCCatalog();
        cat.Shells.Add(Shell("a", ShellSpec.Types.AssetType.Chicken));
        cat.Shells.Add(Shell("b", ShellSpec.Types.AssetType.Depot1));
        Assert.Single(ShellCatalog.ForAssetType(cat, "CHICKEN"));
        Assert.Single(ShellCatalog.ForAssetType(cat, "chicken"));
        Assert.Empty(ShellCatalog.ForAssetType(cat, "HABITAT"));
    }

    [Fact]
    public void ById_FindsShell() {
        var cat = new DLCCatalog();
        cat.Shells.Add(Shell("ei_silo_x", ShellSpec.Types.AssetType.Silo0Small));
        Assert.NotNull(ShellCatalog.ById(cat, "ei_silo_x"));
        Assert.Null(ShellCatalog.ById(cat, "missing"));
    }

    [Fact]
    public void FromCatalog_RealConfig_HasManyShells() {
        string? json = ConfigJson();
        if (json is null) return;

        var cfg = ConfigResponse.Parser.ParseJson(json);
        Assert.NotNull(cfg.DlcCatalog);
        var shells = ShellCatalog.FromCatalog(cfg.DlcCatalog);
        Assert.True(shells.Count > 1000, $"expected thousands of shells, got {shells.Count}");
        Assert.All(shells, s => Assert.Contains("auxbrain.com/dlc", s.Url));
        Assert.NotEmpty(ShellCatalog.ForAssetType(cfg.DlcCatalog, "CHICKEN"));
    }

    [Fact]
    public void Objects_ResolvesChickenWithAnchorAndNoHatsFlag() {
        var cat = new DLCCatalog();
        var chicken = new ShellObjectSpec {
            Identifier = "ei_chicken_base",
            Name = "Base",
            AssetType = ShellSpec.Types.AssetType.Chicken,
            NoHats = false
        };
        chicken.Metadata.Add(new[] { 0.0, 0.5, -0.1, 1.2 });
        chicken.Pieces.Add(new ShellObjectSpec.Types.LODPiece { Lod = 1, Dlc = new DLCItem { Url = "https://www.auxbrain.com/dlc/shellobjects/chicken_lod1.rpoz" } });
        chicken.Pieces.Add(new ShellObjectSpec.Types.LODPiece { Lod = 0, Dlc = new DLCItem { Url = "https://www.auxbrain.com/dlc/shellobjects/chicken_lod0.rpoz" } });
        cat.ShellObjects.Add(chicken);

        var objs = ShellCatalog.Objects(cat);
        var o = Assert.Single(objs);
        Assert.Equal("ei_chicken_base", o.Identifier);
        Assert.Equal("Chicken", o.AssetType);
        Assert.Equal(new[] { 0.0, 0.5, -0.1, 1.2 }, o.Anchor);
        Assert.False(o.NoHats);
        Assert.Equal("https://www.auxbrain.com/dlc/shellobjects/chicken_lod0.rpoz", o.Url);
    }

    [Fact]
    public void Chickens_And_Hats_FilterByAssetType() {
        var cat = new DLCCatalog();
        cat.ShellObjects.Add(Obj("c1", ShellSpec.Types.AssetType.Chicken));
        cat.ShellObjects.Add(Obj("h1", ShellSpec.Types.AssetType.Hat));
        cat.ShellObjects.Add(Obj("h2", ShellSpec.Types.AssetType.Hat));
        Assert.Single(ShellCatalog.Chickens(cat));
        Assert.Equal(2, ShellCatalog.Hats(cat).Count);
    }

    [Fact]
    public void ObjectById_FindsObject() {
        var cat = new DLCCatalog();
        cat.ShellObjects.Add(Obj("ei_hat_x", ShellSpec.Types.AssetType.Hat));
        Assert.NotNull(ShellCatalog.ObjectById(cat, "ei_hat_x"));
        Assert.Null(ShellCatalog.ObjectById(cat, "missing"));
    }

    [Fact]
    public void NoHatsChicken_HasEmptyAnchor() {
        var cat = new DLCCatalog();
        var polish = new ShellObjectSpec { Identifier = "ei_chicken_polish", AssetType = ShellSpec.Types.AssetType.Chicken, NoHats = true };
        polish.Pieces.Add(new ShellObjectSpec.Types.LODPiece { Lod = 0, Dlc = new DLCItem { Url = "https://www.auxbrain.com/dlc/shellobjects/polish.rpoz" } });
        cat.ShellObjects.Add(polish);
        var o = Assert.Single(ShellCatalog.Chickens(cat));
        Assert.True(o.NoHats);
        Assert.Empty(o.Anchor);
    }

    [Fact]
    public void HatWearingChicken_WithoutMetadata_GetsDefaultAnchor() {
        var cat = new DLCCatalog();

        cat.ShellObjects.Add(Obj("ei_chicken_skis", ShellSpec.Types.AssetType.Chicken));
        var o = Assert.Single(ShellCatalog.Chickens(cat));
        Assert.False(o.NoHats);
        Assert.Equal(ShellCatalog.DefaultChickenAnchor, o.Anchor);
    }

    [Fact]
    public void RealConfig_HasChickensWithAnchors_AndHats() {
        string? json = ConfigJson();
        if (json is null) return;
        var cfg = ConfigResponse.Parser.ParseJson(json);
        var chickens = ShellCatalog.Chickens(cfg.DlcCatalog!);
        var hats = ShellCatalog.Hats(cfg.DlcCatalog!);
        Assert.True(chickens.Count > 50, $"expected many chickens, got {chickens.Count}");
        Assert.True(hats.Count > 50, $"expected many hats, got {hats.Count}");
        Assert.Contains(chickens, c => !c.NoHats && c.Anchor.Count == 4);
        Assert.All(chickens, c => Assert.Contains("auxbrain.com/dlc", c.Url));
    }

    [Fact]
    public void Shell_CarriesSetIdentifier() {
        var cat = new DLCCatalog();
        cat.Shells.Add(SetShell("ei_depot_1_neon", ShellSpec.Types.AssetType.Depot1, "neon"));
        Assert.Equal("neon", ShellCatalog.FromCatalog(cat).Single().SetIdentifier);
    }

    [Fact]
    public void Sets_GroupsMembersBySetIdentifier_WithSetName() {
        var cat = new DLCCatalog();
        cat.ShellSets.Add(new ShellSetSpec { Identifier = "neon", Name = "Neon", Decorator = false });
        cat.Shells.Add(SetShell("ei_depot_1_neon", ShellSpec.Types.AssetType.Depot1, "neon"));
        cat.Shells.Add(SetShell("ei_hab_1k_neon", ShellSpec.Types.AssetType.Hab1K, "neon"));
        cat.Shells.Add(SetShell("loner", ShellSpec.Types.AssetType.Silo0Small, ""));

        var sets = ShellCatalog.Sets(cat);
        var set = Assert.Single(sets);
        Assert.Equal("neon", set.Identifier);
        Assert.Equal("Neon", set.Name);
        Assert.False(set.Decorator);
        Assert.Equal(2, set.Members.Count);
        Assert.Contains(set.Members, m => m.AssetType == "Depot1");
        Assert.Contains(set.Members, m => m.AssetType == "Hab1K");
    }

    [Fact]
    public void Decorators_AreSeparateFromSets() {
        var cat = new DLCCatalog();
        cat.ShellSets.Add(new ShellSetSpec { Identifier = "neon", Name = "Neon" });
        cat.Decorators.Add(new ShellSetSpec { Identifier = "lights", Name = "Lights", Decorator = true });
        cat.Shells.Add(SetShell("ei_depot_1_neon", ShellSpec.Types.AssetType.Depot1, "neon"));
        cat.Shells.Add(SetShell("ei_depot_1_lights", ShellSpec.Types.AssetType.Depot1, "lights"));

        var sets = ShellCatalog.Sets(cat);
        var decos = ShellCatalog.Decorators(cat);
        Assert.Single(sets);
        Assert.Equal("neon", sets[0].Identifier);
        var deco = Assert.Single(decos);
        Assert.Equal("lights", deco.Identifier);
        Assert.True(deco.Decorator);
        Assert.Single(deco.Members);
    }

    private static ShellSpec SetShell(string id, ShellSpec.Types.AssetType type, string setId) {
        var s = new ShellSpec {
            Identifier = id,
            SetIdentifier = setId,
            PrimaryPiece = new ShellSpec.Types.ShellPiece {
                AssetType = type,
                Dlc = new DLCItem { Url = $"https://www.auxbrain.com/dlc/shells/{id}.rpoz" }
            }
        };
        return s;
    }

    private static ShellObjectSpec Obj(string id, ShellSpec.Types.AssetType type) {
        var o = new ShellObjectSpec { Identifier = id, AssetType = type };
        o.Pieces.Add(new ShellObjectSpec.Types.LODPiece { Lod = 0, Dlc = new DLCItem { Url = $"https://www.auxbrain.com/dlc/shellobjects/{id}.rpoz" } });
        return o;
    }

    private static ShellSpec Shell(string id, ShellSpec.Types.AssetType type) {
        var s = new ShellSpec {
            Identifier = id,
            PrimaryPiece = new ShellSpec.Types.ShellPiece {
                AssetType = type,
                Dlc = new DLCItem { Url = $"https://www.auxbrain.com/dlc/shells/{id}.rpoz" }
            }
        };
        return s;
    }

    private static string? ConfigJson() {
        string[] candidates = [
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "captures", "config.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "captures", "config.json")
        ];
        foreach (string c in candidates) {
            string full = Path.GetFullPath(c);
            if (File.Exists(full)) return File.ReadAllText(full);
        }

        return null;
    }
}
