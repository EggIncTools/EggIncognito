using System.Text;

namespace EggIncognito.Services.DataApi;

public enum DataProvenance {
    WireFixture,
    Database,
    DerivedExtract,
    Asset
}

public enum DataAccess {
    Public,
    Authenticated
}

public sealed record DeviceTrigger(string AndroidRecipe, string IosRecipe);

public sealed record DataRefresh(bool Egress, DeviceTrigger? Device = null);

public sealed record DataPayload(byte[] Bytes, string ContentType) {
    public static DataPayload Json(string json) => new(Encoding.UTF8.GetBytes(json), "application/json");
    public static DataPayload Png(byte[] bytes) => new(bytes, "image/png");
}

public sealed record DataProduceContext(HttpContext Http, string? Name) {
    public IServiceProvider Services => Http.RequestServices;
}

public sealed record DataSource(
    string Id,
    string Group,
    string DisplayName,
    string Description,
    DataProvenance Provenance,
    DataAccess Access,
    string? WireRoute,
    string? Feed,
    DataRefresh Refresh,
    bool AcceptsName,
    Func<DataProduceContext, CancellationToken, Task<DataPayload?>> Produce,
    Func<string, byte[]>? BuildEgressRequest = null,
    string? Extends = null,
    bool Listed = true,
    string? Feature = null);
