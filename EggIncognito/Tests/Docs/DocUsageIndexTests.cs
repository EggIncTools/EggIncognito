using EggIncognito.Core.Services;
using EggIncognito.Models.Docs;
using EggIncognito.Services.Docs;

namespace EggIncognito.Tests.Docs;

public sealed class DocUsageIndexTests : IDisposable {
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private const string YamlText = """
                                    routes:
                                      - path: ei/first_contact_secure
                                        request: EggIncFirstContactRequest
                                        requestWrapped: true
                                        response: EggIncFirstContactResponse
                                        responseWrapped: true
                                      - path: ei/get_periodicals
                                        request: GetPeriodicalsRequest
                                        response: PeriodicalsResponse
                                      - path: ei/legacy_wrapped
                                        request: AuthenticatedMessage
                                        response: AuthenticatedMessage
                                    """;

    private DocUsageIndex Build(IBinaryRouteProvider? binary = null) {
        string path = _tmp.Combine($"docusage-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, YamlText);
        return new DocUsageIndex(new RouteCatalog(path), new ProtoReflection(), binary);
    }

    [Fact]
    public void AuthenticatedMessage_HasWrapUses() {
        var uses = Build().EndpointsUsing(RouteCatalog.WrapperMessage);

        Assert.Contains(uses, u => u.Path == "ei/first_contact_secure" && u.Role == MessageUseRole.WrapsBoth);
        Assert.Contains(uses, u => u.Path == "ei/legacy_wrapped" && u.Role == MessageUseRole.WrapsBoth);
        Assert.DoesNotContain(uses, u => u.Path == "ei/get_periodicals");
    }

    [Fact]
    public void PlainRequestType_HasRequestUse() {
        var uses = Build().EndpointsUsing("EggIncFirstContactRequest");

        var use = Assert.Single(uses);
        Assert.Equal("ei/first_contact_secure", use.Path);
        Assert.Equal(MessageUseRole.Request, use.Role);
        Assert.NotNull(use.Route);
        Assert.Equal("ei/first_contact_secure", use.Route.Path);
    }

    [Fact]
    public void PlainResponseType_HasResponseUse() {
        var uses = Build().EndpointsUsing("PeriodicalsResponse");

        var use = Assert.Single(uses);
        Assert.Equal("ei/get_periodicals", use.Path);
        Assert.Equal(MessageUseRole.Response, use.Role);
    }

    [Fact]
    public void UnknownMessage_HasNoUses() {
        var index = Build();
        Assert.Empty(index.EndpointsUsing("NoSuchMessage"));
        Assert.Empty(index.ReferencedBy("NoSuchMessage"));
        Assert.Empty(index.FieldsOf("NoSuchMessage"));
        Assert.False(index.IsKnown("NoSuchMessage"));
    }

    [Fact]
    public void Backup_IsReferencedByFirstContactResponse() {
        var refs = Build().ReferencedBy("Backup");

        Assert.Contains(refs, r => r.Message == "EggIncFirstContactResponse" && r.Field == "backup" && !r.Repeated);
    }

    [Fact]
    public void FieldsOf_KnownMessage_ListsSchemaFields() {
        var index = Build();
        var fields = index.FieldsOf("EggIncFirstContactResponse");

        Assert.True(index.IsKnown("EggIncFirstContactResponse"));
        Assert.Contains(fields, f => f.Name == "backup" && f.Type == "message" && f.MessageType == "Backup");
    }

    [Fact]
    public void NestedType_IsKnownAndReferencedByItsParent() {
        var index = Build();

        Assert.True(index.IsKnown("Backup.Game"));
        Assert.Contains(index.ReferencedBy("Backup.Game"), r => r.Message == "Backup" && r.Field == "game");
        Assert.Contains("Backup.Game", index.NestedOf("Backup"));
        Assert.Equal("Backup", index.ParentOf("Backup.Game"));
        Assert.Null(index.ParentOf("Backup"));
    }

    [Fact]
    public void BinaryOnlyRoute_IsAStaticUse() {
        var index = Build(new FakeBinaryRouteProvider(
            new BinaryRouteInfo("ei/mission", "getMission", "MissionInfo", null, true, false, "1.37", "ios",
                DateTimeOffset.UnixEpoch),
            new BinaryRouteInfo("ei/get_periodicals", "getPeriodicals", "GetPeriodicalsRequest", "PeriodicalsResponse",
                false, false, "1.37", "ios", DateTimeOffset.UnixEpoch)));

        var use = Assert.Single(index.EndpointsUsing("MissionInfo"));
        Assert.Equal("ei/mission", use.Path);
        Assert.Equal(MessageUseRole.Request, use.Role);
        Assert.Null(use.Route);
        Assert.True(use.Locked);

        var periodicals = Assert.Single(index.EndpointsUsing("PeriodicalsResponse"));
        Assert.NotNull(periodicals.Route);
    }
}
