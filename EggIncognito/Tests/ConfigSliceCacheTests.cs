using System.Text.Json;
using System.Text.Json.Nodes;
using EggIncognito.Core.Services;
using EggIncognito.Services.DataApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public class ConfigSliceCacheTests {
    private static IServiceProvider BuildServices(string contentRoot) {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ContentRoot"] = contentRoot })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(new EndpointStore(
            new FileEndpointSource(Path.Combine(contentRoot, "Endpoints")),
            null,
            NullLogger<EndpointStore>.Instance));
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
    public async Task Slice_ReturnsPayloadWithProvenance_AndNullsForUnknownFieldOrMissingFile() {
        using var tmp = new TempDir();
        string root = tmp.Path;
        WriteFixture(root, """{ "dlcCatalog": { "items": [1, 2], "shells": [{"id": "a"}] } }""");
        var services = BuildServices(root);
        var store = services.GetRequiredService<EndpointStore>();

        var cache = new ConfigSliceCache();
        var payload = await cache.SliceAsync(store, "ei/get_config", "items", CancellationToken.None);
        Assert.NotNull(payload);
        var doc = ParseObject(payload.Bytes);
        Assert.Equal(2, doc["items"]!.AsArray().Count);
        Assert.Equal("ei/get_config", doc["provenance"]!["source"]!.GetValue<string>());
        Assert.Equal("dlcCatalog.items", doc["provenance"]!["path"]!.GetValue<string>());

        Assert.Null(await cache.SliceAsync(store, "ei/get_config", "decorators", CancellationToken.None));

        string missingRoot = tmp.File("missing-root");
        var missingServices = BuildServices(missingRoot);
        var missingStore = missingServices.GetRequiredService<EndpointStore>();
        Assert.Null(await cache.SliceAsync(missingStore, "ei/get_config", "items", CancellationToken.None));
    }

    [Fact]
    public async Task Slice_ReflectsRewrittenFileAfterStampChanges() {
        using var tmp = new TempDir();
        string root = tmp.Path;
        string path = WriteFixture(root, """{ "dlcCatalog": { "items": [1] } }""");
        var services = BuildServices(root);
        var store = services.GetRequiredService<EndpointStore>();

        var cache = new ConfigSliceCache();
        var first = await cache.SliceAsync(store, "ei/get_config", "items", CancellationToken.None);
        Assert.NotNull(first);
        Assert.Single(ParseObject(first.Bytes)["items"]!.AsArray());

        File.WriteAllText(path, """{ "dlcCatalog": { "items": [1, 2, 3] } }""");
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(5));

        var rebuiltServices = BuildServices(root);
        var rebuiltStore = rebuiltServices.GetRequiredService<EndpointStore>();
        var second = await cache.SliceAsync(rebuiltStore, "ei/get_config", "items", CancellationToken.None);
        Assert.NotNull(second);
        Assert.Equal(3, ParseObject(second.Bytes)["items"]!.AsArray().Count);
    }
}
