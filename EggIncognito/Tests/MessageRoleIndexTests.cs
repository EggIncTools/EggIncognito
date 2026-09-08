using EggIncognito.Core.Services;
using EggIncognito.Models.Inspector;
using EggIncognito.Services.Inspector;

namespace EggIncognito.Tests;

public sealed class MessageRoleIndexTests : IDisposable {
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private const string YamlText = """
                                    routes:
                                      - path: ei/first_contact_secure
                                        request: EggIncFirstContactRequest
                                        requestWrapped: true
                                        response: EggIncFirstContactResponse
                                        responseWrapped: true
                                      - path: ei/save_backup_secure
                                        request: Backup
                                        requestWrapped: true
                                        response:
                                        responseWrapped: true
                                    """;

    private MessageRoles Build(IBinaryRouteProvider? binary = null) {
        string path = _tmp.Combine($"roles-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, YamlText);
        return new MessageRoleIndex(new ProtoReflection(), new RouteCatalog(path), binary).Snapshot();
    }

    [Fact]
    public void RouteUsage_PutsBackupInRequestTypes() {
        var roles = Build();
        Assert.Contains("Backup", roles.Request);
        Assert.DoesNotContain("Backup", roles.Other);
    }

    [Fact]
    public void Suffix_PutsFirstContactResponseInResponseTypes() {
        var roles = Build();
        Assert.Contains("EggIncFirstContactResponse", roles.Response);
        Assert.Contains("EggIncFirstContactRequest", roles.Request);
    }

    [Fact]
    public void Wrapper_IsExcludedEverywhere() {
        var roles = Build();
        Assert.DoesNotContain(RouteCatalog.WrapperMessage, roles.Request);
        Assert.DoesNotContain(RouteCatalog.WrapperMessage, roles.Response);
        Assert.DoesNotContain(RouteCatalog.WrapperMessage, roles.Other);
    }

    [Fact]
    public void Others_HoldEverythingUnclassified() {
        var roles = Build();
        Assert.Contains("Contract", roles.Other);
        Assert.Empty(roles.Other.Intersect(roles.Request));
        Assert.Empty(roles.Other.Intersect(roles.Response));
    }

    [Fact]
    public void BinaryUsage_PromotesTypeOutOfOthers() {
        var binaryRoute = new BinaryRouteInfo("ei/mission", "getMission", "MissionInfo", null, true, false, "1.37", "ios",
            DateTimeOffset.UnixEpoch);

        Assert.Contains("MissionInfo", Build().Other);
        var withBinary = Build(new FakeBinaryRouteProvider(binaryRoute));
        Assert.Contains("MissionInfo", withBinary.Request);
        Assert.DoesNotContain("MissionInfo", withBinary.Other);
    }
}
