using System.Text.Json;
using EggIncognito.Core.Services;

namespace EggIncognito.Tests;

public sealed class OpenApiBuilderTests {
    private static readonly ProtoReflection Reflection = new();

    private static AuxbrainEntry Entry(
        string path,
        string? request = "EggIncFirstContactRequest",
        string? response = "EggIncFirstContactResponse",
        bool requestWrapped = false,
        bool responseWrapped = false,
        bool pathParam = false,
        AuxbrainStatus status = AuxbrainStatus.Ok,
        IReadOnlyList<string>? aliases = null) =>
        new(path, path.Split('/')[0], request, response,
            requestWrapped, responseWrapped, pathParam, status) { Aliases = aliases ?? [] };

    private static JsonDocument Build(params AuxbrainEntry[] entries) =>
        JsonDocument.Parse(OpenApiBuilder.BuildJson(entries, Reflection));

    [Fact]
    public void EveryEntryPath_PresentUnderPaths_WithPost() {
        using var doc = Build(
            Entry("ei/first_contact"),
            Entry("ei_afx/config", "ArtifactsConfigurationRequest", "ArtifactsConfigurationResponse"));
        var paths = doc.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/ei/first_contact").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/ei_afx/config").TryGetProperty("post", out _));
    }

    [Fact]
    public void RequestBody_IsFormEncoded_Base64DataField() {
        using var doc = Build(Entry("ei/first_contact"));
        var body = doc.RootElement.GetProperty("paths").GetProperty("/ei/first_contact")
            .GetProperty("post").GetProperty("requestBody");

        Assert.True(body.GetProperty("required").GetBoolean());
        var schema = body.GetProperty("content")
            .GetProperty("application/x-www-form-urlencoded").GetProperty("schema");
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Equal("data", schema.GetProperty("required")[0].GetString());

        var data = schema.GetProperty("properties").GetProperty("data");
        Assert.Equal("string", data.GetProperty("type").GetString());
        Assert.Equal("byte", data.GetProperty("format").GetString());
    }

    [Fact]
    public void ResponseSchema_RefsComponent_WithExpectedFields() {
        using var doc = Build(Entry("ei/first_contact"));
        var schema = doc.RootElement.GetProperty("paths").GetProperty("/ei/first_contact")
            .GetProperty("post").GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal("#/components/schemas/EggIncFirstContactResponse", schema.GetProperty("$ref").GetString());

        var component = doc.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("EggIncFirstContactResponse");
        var props = component.GetProperty("properties");

        Assert.Equal("string", props.GetProperty("eiUserId").GetProperty("type").GetString());

        Assert.Equal("#/components/schemas/Backup", props.GetProperty("backup").GetProperty("$ref").GetString());
        Assert.True(doc.RootElement.GetProperty("components").GetProperty("schemas")
            .TryGetProperty("Backup", out _));
    }

    [Fact]
    public void RepeatedField_MapsToArray() {
        using var doc = Build(Entry("ei/first_contact"));
        var ids = doc.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("EggIncFirstContactResponse").GetProperty("properties")
            .GetProperty("idsTransferred");
        Assert.Equal("array", ids.GetProperty("type").GetString());
        Assert.Equal("string", ids.GetProperty("items").GetProperty("type").GetString());
    }

    [Fact]
    public void EnumField_MapsToStringEnumOfNames() {
        using var doc = Build(Entry("ei_afx/mission", "MissionRequest", "MissionInfo"));
        var ship = doc.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("MissionInfo").GetProperty("properties").GetProperty("ship");
        Assert.Equal("string", ship.GetProperty("type").GetString());
        var names = ship.GetProperty("enum").EnumerateArray().Select(v => v.GetString()).ToList();
        Assert.Contains("HENERPRISE", names);
    }

    [Fact]
    public void WrappedEntry_DocumentsSigningInDescriptions() {
        using var doc = Build(
            Entry("ei/first_contact_secure", requestWrapped: true, responseWrapped: true));
        var op = doc.RootElement.GetProperty("paths").GetProperty("/ei/first_contact_secure")
            .GetProperty("post");
        Assert.True(op.GetProperty("x-eggincognito-request-wrapped").GetBoolean());
        Assert.Contains("AuthenticatedMessage", op.GetProperty("description").GetString());
    }

    [Fact]
    public void AliasedEntry_ListsAliasesAsVendorExtension() {
        using var doc = Build(Entry("ei/new_name", aliases: ["ei/old_name"]));
        var op = doc.RootElement.GetProperty("paths").GetProperty("/ei/new_name").GetProperty("post");
        Assert.Equal("ei/old_name", op.GetProperty("x-eggincognito-aliases")[0].GetString());
    }

    [Fact]
    public void PathParamEntry_EmitsEidVariantPath() {
        using var doc = Build(
            Entry("ei_ctx/get_contract_evaluation", "BasicRequestInfo", "ContractEvaluation",
                pathParam: true));
        var variant = doc.RootElement.GetProperty("paths")
            .GetProperty("/ei_ctx/get_contract_evaluation/{eid}").GetProperty("post");
        var param = variant.GetProperty("parameters")[0];
        Assert.Equal("eid", param.GetProperty("name").GetString());
        Assert.Equal("path", param.GetProperty("in").GetString());
    }

    [Fact]
    public void NullResponseType_OmitsContent_KeepsDescription() {
        using var doc = Build(Entry("ei/showcase_vote", null, null,
            true, true));
        var ok = doc.RootElement.GetProperty("paths").GetProperty("/ei/showcase_vote")
            .GetProperty("post").GetProperty("responses").GetProperty("200");
        Assert.False(ok.TryGetProperty("content", out _));
        Assert.False(string.IsNullOrEmpty(ok.GetProperty("description").GetString()));
    }

    [Fact]
    public void FullRealCatalog_Builds_AndEveryRefResolves() {
        string root = RepoRoot();
        string yamlPath = Path.Combine(root, "EggIncognito", "RouteMap", "routes.yaml");
        string defaultsDir = Path.Combine(root, "EggIncognito", "Endpoints", "default");

        var entries = AuxbrainCatalog.Build(
            new RouteCatalog(yamlPath).All(),
            EndpointStatus.Classify(yamlPath, defaultsDir));
        Assert.True(entries.Count >= 64, $"expected >= 64 catalog entries, got {entries.Count}");

        using var doc = JsonDocument.Parse(OpenApiBuilder.BuildJson(entries, Reflection));
        var paths = doc.RootElement.GetProperty("paths");
        foreach (var e in entries) {
            Assert.True(paths.GetProperty("/" + e.Path).TryGetProperty("post", out _),
                $"missing post operation for {e.Path}");
        }

        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        var refs = new List<string>();
        CollectRefs(doc.RootElement, refs);
        Assert.NotEmpty(refs);
        foreach (string r in refs.Distinct()) {
            Assert.StartsWith("#/components/schemas/", r);
            string name = r["#/components/schemas/".Length..];
            Assert.True(schemas.TryGetProperty(name, out _), $"unresolved $ref {r}");
        }
    }

    private static void CollectRefs(JsonElement element, List<string> refs) {
        switch (element.ValueKind) {
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject()) {
                    if (p.Name == "$ref" && p.Value.ValueKind == JsonValueKind.String)
                        refs.Add(p.Value.GetString()!);
                    else CollectRefs(p.Value, refs);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) CollectRefs(item, refs);
                break;
        }
    }

    private static string RepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
