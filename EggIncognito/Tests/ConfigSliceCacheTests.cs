using System.Text.Json;
using System.Text.Json.Nodes;
using EggIncognito.Services.DataApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class ConfigSliceCacheTests {
    private static IServiceProvider BuildServices(string contentRoot) {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ContentRoot"] = contentRoot })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        return services.BuildServiceProvider();
    }

    private static string WriteFixture(string contentRoot, string json) {
        string dir = Path.Combine(contentRoot, "Endpoints", "default", "ei");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "get_config.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static JsonObject ParseObject(byte[] bytes) {
        var reader = new Utf8JsonReader(bytes);
        return (JsonObject)JsonNode.Parse(ref reader)!;
    }

    [Fact]
    public void Slice_ReturnsPayloadWithProvenance_AndNullsForUnknownFieldOrMissingFile() {
        using var tmp = new TempDir();
        string root = tmp.Path;
        var services = BuildServices(root);
        WriteFixture(root, """{ "dlcCatalog": { "items": [1, 2], "shells": [{"id": "a"}] } }""");

        var cache = new ConfigSliceCache();
        var payload = cache.Slice(services, "ei/get_config", "items");
        Assert.NotNull(payload);
        var doc = ParseObject(payload.Bytes);
        Assert.Equal(2, doc["items"]!.AsArray().Count);
        Assert.Equal("ei/get_config", doc["provenance"]!["source"]!.GetValue<string>());
        Assert.Equal("dlcCatalog.items", doc["provenance"]!["path"]!.GetValue<string>());

        Assert.Null(cache.Slice(services, "ei/get_config", "decorators"));

        string missingRoot = tmp.File("missing-root");
        var missingServices = BuildServices(missingRoot);
        Assert.Null(cache.Slice(missingServices, "ei/get_config", "items"));
    }

    [Fact]
    public void Slice_ReflectsRewrittenFileAfterStampChanges() {
        using var tmp = new TempDir();
        string root = tmp.Path;
        var services = BuildServices(root);
        string path = WriteFixture(root, """{ "dlcCatalog": { "items": [1] } }""");

        var cache = new ConfigSliceCache();
        var first = cache.Slice(services, "ei/get_config", "items");
        Assert.NotNull(first);
        Assert.Single(ParseObject(first.Bytes)["items"]!.AsArray());

        File.WriteAllText(path, """{ "dlcCatalog": { "items": [1, 2, 3] } }""");
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(5));

        var second = cache.Slice(services, "ei/get_config", "items");
        Assert.NotNull(second);
        Assert.Equal(3, ParseObject(second.Bytes)["items"]!.AsArray().Count);
    }
}
