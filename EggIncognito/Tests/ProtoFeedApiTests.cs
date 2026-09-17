using System.Net;
using EggIdentity.Contract;
using EggIncognito.Controllers;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Protos;
using EggIncognito.Services;
using EggIncognito.Services.Feed;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Tests;

public class ProtoFeedApiTests {
    private static FeedSubscriptionStore UnconnectedStore() {
        var opts = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1").Options;
        return new FeedSubscriptionStore(new EggIncognitoDbContext(opts));
    }

    private static ProtoFeedController Controller(IServiceProvider services,
        Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(services, new StubHttpFactory(new StubHttpMessageHandler(respond)));

    private static int Status(IActionResult r) => ((IStatusCodeActionResult)r).StatusCode ?? 200;

    [Fact]
    public async Task Create_NoStore_Returns503() {
        var services = new MapServices(new Dictionary<Type, object?> {
            [typeof(ICurrentUser)] = new StubUser("42")
        });
        var c = Controller(services, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Create(new FeedCreateReq(
            "https://discord.com/api/webhooks/1/abc", null, null, null, null), CancellationToken.None);
        Assert.Equal(503, Status(r));
    }

    [Fact]
    public async Task Create_Anon_Returns401() {
        var c = Controller(new MapServices([]), _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Create(new FeedCreateReq(
            "https://discord.com/api/webhooks/1/abc", null, null, null, null), CancellationToken.None);
        Assert.Equal(401, Status(r));
    }

    [Fact]
    public async Task Create_BadUrl_Returns400() {
        var services = new MapServices(new Dictionary<Type, object?> {
            [typeof(FeedSubscriptionStore)] = UnconnectedStore(),
            [typeof(ICurrentUser)] = new StubUser("42")
        });
        var c = Controller(services, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Create(new FeedCreateReq(
            "https://evil.example.com/hook", null, null, null, null), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(r);
        Assert.Equal(400, Status(r));
    }

    [Fact]
    public async Task Create_EmptyUrl_Returns400() {
        var services = new MapServices(new Dictionary<Type, object?> {
            [typeof(FeedSubscriptionStore)] = UnconnectedStore(),
            [typeof(ICurrentUser)] = new StubUser("42")
        });
        var c = Controller(services, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Create(new FeedCreateReq("", null, null, null, null), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(r);
    }

    [Fact]
    public async Task Mine_Anon_Returns401() {
        var c = Controller(new MapServices([]), _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Mine(CancellationToken.None);
        Assert.Equal(401, Status(r));
    }

    [Fact]
    public async Task Mine_NoStore_Returns503() {
        var services = new MapServices(new Dictionary<Type, object?> {
            [typeof(ICurrentUser)] = new StubUser("42")
        });
        var c = Controller(services, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Mine(CancellationToken.None);
        Assert.Equal(503, Status(r));
    }

    [Fact]
    public async Task Delete_Anon_Returns401() {
        var c = Controller(new MapServices([]), _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Delete(1, CancellationToken.None);
        Assert.Equal(401, Status(r));
    }

    [Fact]
    public async Task Delete_NoStore_Returns503() {
        var services = new MapServices(new Dictionary<Type, object?> {
            [typeof(ICurrentUser)] = new StubUser("42")
        });
        var c = Controller(services, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Delete(1, CancellationToken.None);
        Assert.Equal(503, Status(r));
    }

    [Fact]
    public async Task Update_Anon_Returns401() {
        var c = Controller(new MapServices([]), _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Update(1, new FeedUpdateReq(["android"], "new_version", true, null),
            CancellationToken.None);
        Assert.Equal(401, Status(r));
    }

    [Fact]
    public async Task Update_NoStore_Returns503() {
        var services = new MapServices(new Dictionary<Type, object?> {
            [typeof(ICurrentUser)] = new StubUser("42")
        });
        var c = Controller(services, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Update(1, new FeedUpdateReq(null, null, null, null), CancellationToken.None);
        Assert.Equal(503, Status(r));
    }

    [Theory]
    [InlineData("https://discord.com/api/webhooks/123456789/abcdEFGHtoken1234", "webhooks/123456789/...1234")]
    [InlineData("https://discord.com/api/webhooks/999/tok", "webhooks/999/...tok")]
    public void MaskWebhook_DiscordUrl_ShowsIdHidesToken(string url, string expected) {
        string masked = ProtoFeedController.MaskWebhook(url);
        Assert.Equal(expected, masked);

        Assert.DoesNotContain("abcdEFGHtoken1234", masked);
    }

    private static FeedSubscription Sub(string eventKind, params string[] filters) => new() {
        Id = 1,
        EventKind = eventKind,
        Filters = filters
    };

    [Fact]
    public void ResolveFilters_NullRequest_KeepsStoredFilters() {
        var sub = Sub(FeedEventKinds.ProtoBuild, FeedEventKinds.FilterRequireProto);

        Assert.Equal([FeedEventKinds.FilterRequireProto], ProtoFeedController.ResolveFilters(sub, null));
    }

    [Fact]
    public void ResolveFilters_NullRequest_DoesNotReEnableGuardsTheOwnerTurnedOff() {
        var sub = Sub(FeedEventKinds.ProtoBuild);

        Assert.Empty(ProtoFeedController.ResolveFilters(sub, null));
    }

    [Fact]
    public void ResolveFilters_NullRequest_KeepsConfigOptIn() {
        var sub = Sub(FeedEventKinds.ConfigChanged, FeedEventKinds.FilterRequireAspects);

        Assert.Equal([FeedEventKinds.FilterRequireAspects], ProtoFeedController.ResolveFilters(sub, null));
    }

    [Fact]
    public void ResolveFilters_LegacyPeriodicalsKind_NormalizesAgainstConfig() {
        var sub = Sub(FeedEventKinds.LegacyPeriodicalsChanged, FeedEventKinds.FilterRequireAspects);

        Assert.Equal([FeedEventKinds.FilterRequireAspects], ProtoFeedController.ResolveFilters(sub, null));
    }

    [Fact]
    public void ResolveFilters_EmptyRequest_ClearsFilters() {
        var sub = Sub(FeedEventKinds.ProtoBuild, FeedEventKinds.FilterRequireProto, FeedEventKinds.FilterSaneBuild);

        Assert.Empty(ProtoFeedController.ResolveFilters(sub, []));
    }

    [Fact]
    public void ResolveFilters_SuppliedRequest_NormalizesAgainstTheEventKind() {
        var sub = Sub(FeedEventKinds.ProtoBuild, FeedEventKinds.FilterRequireProto);

        Assert.Equal([FeedEventKinds.FilterSaneBuild],
            ProtoFeedController.ResolveFilters(sub, [FeedEventKinds.FilterSaneBuild, "not_a_filter"]));
    }

    [Fact]
    public void MaskWebhook_NonDiscord_GenericTail() {
        string masked = ProtoFeedController.MaskWebhook("https://example.com/hook/SECRETvalue");
        Assert.Equal("...Tvalue", masked);
        Assert.DoesNotContain("SECRETvalue", masked);
    }

    private sealed class MapServices(Dictionary<Type, object?> map) : IServiceProvider {
        public object? GetService(Type serviceType) => map.GetValueOrDefault(serviceType);
    }

    private sealed class StubUser(string? discordId) : ICurrentUser {
        public bool IsAuthenticated => discordId is not null;
        public Guid? UserId => discordId is not null ? Guid.Parse("00000000-0000-0000-0000-000000000001") : null;
        public string? DiscordId => discordId;
        public string? Username => null;
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => UserRole.Viewer;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => false;
    }
}
