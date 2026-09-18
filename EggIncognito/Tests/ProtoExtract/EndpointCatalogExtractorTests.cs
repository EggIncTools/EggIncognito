using EggIncognito.Core.Services;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class EndpointCatalogExtractorTests {
    private static bool TryLoadAndroid(out byte[] bin, out string contentRoot) {
        bin = [];
        contentRoot = "";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EggIncognito.slnx"))) dir = dir.Parent;
        if (dir is null) return false;

        contentRoot = Path.Combine(dir.FullName, "EggIncognito");
        string path = Path.Combine(contentRoot, "captures", "egginc-android-1.37.so");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 1_000_000) return false;

        bin = File.ReadAllBytes(path);
        return true;
    }

    private static EndpointCatalogExtractor.EndpointDescriptor? ByPath(
        EndpointCatalogExtractor.Result r, string path) =>
        r.Endpoints.FirstOrDefault(e => e.Path == path);

    [Fact]
    public void Extract_TranslationPack_IsFullyWrapped() {
        if (!TryLoadAndroid(out var bin, out _)) return;
        var r = EndpointCatalogExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);

        var e = ByPath(r, "ei_i18n/get_translation_pack");
        Assert.NotNull(e);
        Assert.Equal("TranslationPackRequest", e!.RequestType);
        Assert.Equal("TranslationPackResponse", e.ResponseType);
        Assert.True(e.RequestWrapped);
        Assert.True(e.ResponseWrapped);
    }

    [Fact]
    public void Extract_Translations_IsFullyWrapped() {
        if (!TryLoadAndroid(out var bin, out _)) return;
        var r = EndpointCatalogExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);

        var e = ByPath(r, "ei_i18n/get_translations");
        Assert.NotNull(e);
        Assert.Equal("TranslationRequest", e!.RequestType);
        Assert.Equal("TranslationResponse", e.ResponseType);
        Assert.True(e.RequestWrapped);
        Assert.True(e.ResponseWrapped);
    }

    [Fact]
    public void Extract_GetConfig_PathAndRequestType() {
        if (!TryLoadAndroid(out var bin, out _)) return;
        var r = EndpointCatalogExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);

        var e = ByPath(r, "ei/get_config");
        Assert.NotNull(e);
        Assert.Equal("getConfig", e!.Method);
        Assert.Equal("ConfigRequest", e.RequestType);
    }

    [Fact]
    public void Extract_ProducesSaneCount() {
        if (!TryLoadAndroid(out var bin, out _)) return;
        var r = EndpointCatalogExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.True(r.Endpoints.Count >= 40, $"only {r.Endpoints.Count} endpoints");
    }

    [Fact]
    public void Extract_MachO_ProducesSaneCount() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = EndpointCatalogExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.True(r.Endpoints.Count >= 40, $"only {r.Endpoints.Count} endpoints");
    }

    [Fact]
    public void Extract_MachO_GetConfig_PathAndRequestType() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = EndpointCatalogExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);

        var e = ByPath(r, "ei/get_config");
        Assert.NotNull(e);
        Assert.Equal("getConfig", e!.Method);
        Assert.Equal("ConfigRequest", e.RequestType);
    }

    private static readonly string[] VerifiedDriftPaths = [
        "ei_i18n/get_translation_pack",
        "ei_i18n/get_translations",
    ];

    [Fact]
    public void Extract_WrappedFlags_MatchRouteCatalog() {
        if (!TryLoadAndroid(out var bin, out var contentRoot)) return;
        var r = EndpointCatalogExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);

        var routes = RouteCatalog.ForRepo(contentRoot).All()
            .ToDictionary(x => x.Path, StringComparer.Ordinal);

        var mismatches = new List<string>();
        foreach (var e in r.Endpoints) {
            if (e.Path is null || !VerifiedDriftPaths.Contains(e.Path, StringComparer.Ordinal)) continue;
            if (!routes.TryGetValue(e.Path, out var route)) {
                mismatches.Add($"{e.Path}: no route");
                continue;
            }

            if (route.RequestWrapped != e.RequestWrapped) {
                mismatches.Add($"{e.Path}: route.requestWrapped={route.RequestWrapped} binary={e.RequestWrapped}");
            }

            if (route.ResponseWrapped != e.ResponseWrapped) {
                mismatches.Add($"{e.Path}: route.responseWrapped={route.ResponseWrapped} binary={e.ResponseWrapped}");
            }
        }

        Assert.Empty(mismatches);
    }
}
