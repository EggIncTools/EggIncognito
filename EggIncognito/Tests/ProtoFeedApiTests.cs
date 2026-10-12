using System.Net;
using System.Text.Json;
using EggIdentity.Auth;
using EggIdentity.Filters;
using EggIncognito.Controllers;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Protos;
using EggIncognito.Services.Feed;
using EggIncognito.Services.Feed.Kinds;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Tests;

public class ProtoFeedApiTests {
    private static FeedSubscriptionStore UnconnectedStore() {
        var opts = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1").Options;
        return new FeedSubscriptionStore(new EggIncognitoDbContext(opts), TimeProvider.System);
    }

    private static ProtoFeedController Controller(ICurrentUser user,
        Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(user, new StubHttpFactory(new StubHttpMessageHandler(respond)));

    private static int Status(IActionResult r) => ((IStatusCodeActionResult)r).StatusCode ?? 200;

    [Fact]
    public async Task Create_Anon_Returns401() {
        var c = Controller(StubUser(null), _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Create(new FeedCreateReq(
            "https://discord.com/api/webhooks/1/abc", null, null), UnconnectedStore(), null,
            CancellationToken.None);
        Assert.Equal(401, Status(r));
    }

    [Fact]
    public async Task Create_BadUrl_Returns400() {
        var c = Controller(StubUser("42"), _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Create(new FeedCreateReq(
            "https://evil.example.com/hook", null, null), UnconnectedStore(), null,
            CancellationToken.None);
        Assert.Equal(400, Status(r));
    }

    [Fact]
    public async Task Create_EmptyUrl_Returns400() {
        var c = Controller(StubUser("42"), _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Create(new FeedCreateReq("", null, null), UnconnectedStore(), null,
            CancellationToken.None);
        Assert.Equal(400, Status(r));
    }

    [Fact]
    public async Task Mine_Anon_Returns401() {
        var c = Controller(StubUser(null), _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Mine(UnconnectedStore(), CancellationToken.None);
        Assert.Equal(401, Status(r));
    }

    [Fact]
    public async Task Delete_Anon_Returns401() {
        var c = Controller(StubUser(null), _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Delete(1, UnconnectedStore(), null, CancellationToken.None);
        Assert.Equal(401, Status(r));
    }

    [Fact]
    public async Task Update_Anon_Returns401() {
        var c = Controller(StubUser(null), _ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await c.Update(1, new FeedUpdateReq(true, null), UnconnectedStore(), null,
            CancellationToken.None);
        Assert.Equal(401, Status(r));
    }

    [Theory]
    [InlineData("https://discord.com/api/webhooks/123456789/abcdEFGHtoken1234", "webhooks/123456789/...1234")]
    [InlineData("https://discord.com/api/webhooks/999/tok", "webhooks/999/...tok")]
    public void MaskWebhook_DiscordUrl_ShowsIdHidesToken(string url, string expected) {
        string masked = ProtoFeedController.MaskWebhook(url);
        Assert.Equal(expected, masked);

        Assert.DoesNotContain("abcdEFGHtoken1234", masked);
    }

    [Fact]
    public void ParseFilter_NoBody_FallsBackToTheGivenSet() {
        var fallback = FeedEventKinds.Proto.Defaults;

        Assert.Same(fallback, ProtoFeedController.ParseFilter(null, fallback));
    }

    [Fact]
    public void ParseFilter_EmptyObject_IsTheEmptySet_SoAnOwnerCanClearEveryCondition() {
        var parsed = ProtoFeedController.ParseFilter(JsonElement.Parse("{}"), FeedEventKinds.Proto.Defaults);

        Assert.True(parsed.IsEmpty);
    }

    [Fact]
    public void Problems_UnknownField_IsRejected_WithTheRow() {
        var set = FeedFilter.Rows([new FilterCondition("not_a_field", FilterOps.Eq, "x")]);

        string? problems = ProtoFeedController.Problems(FeedEventKinds.Proto, set);

        Assert.NotNull(problems);
        Assert.Contains("row 1", problems, StringComparison.Ordinal);
    }

    [Fact]
    public void Problems_EmptyValue_IsRejected() {
        var set = FeedFilter.Rows([new FilterCondition(ProtoBuildKind.AppVersion, FilterOps.Gte, "")]);

        Assert.NotNull(ProtoFeedController.Problems(FeedEventKinds.Proto, set));
    }

    [Fact]
    public void Problems_EveryKindDefault_IsValid() {
        foreach (var kind in FeedEventKinds.All) Assert.Null(ProtoFeedController.Problems(kind, kind.Defaults));
    }

    [Fact]
    public void FilterJson_LegacyRow_ConvertsFromTriggerAndFilters() {
        var sub = new FeedSubscription {
            EventKind = FeedEventKinds.ProtoBuild,
            Trigger = FeedEventKinds.TriggerProtoChanged,
            Platforms = ["ios"],
            Filters = [FeedEventKinds.FilterRequireProto]
        };

        var set = FilterCodec.Deserialize(ProtoFeedController.FilterJson(sub).GetRawText());

        Assert.Contains(set.And, c => c.Field == ProtoBuildKind.ProtoChangedField && c.Op == FilterOps.True);
        Assert.Contains(set.And, c => c.Field == ProtoBuildKind.Platform && c.Value == "ios");
        Assert.Contains(set.And, c => c.Field == ProtoBuildKind.FlawsField && c.Op == FilterOps.NotContains);
        Assert.Null(ProtoFeedController.Problems(FeedEventKinds.Proto, set));
    }

    [Fact]
    public void FilterJson_StoredSet_WinsOverLegacyColumns() {
        var stored = FeedFilter.Rows([FeedFilter.Is(ProtoBuildKind.Platform, "android")]);
        var sub = new FeedSubscription {
            EventKind = FeedEventKinds.ProtoBuild,
            Trigger = FeedEventKinds.TriggerSuspect,
            FilterSet = FilterCodec.Serialize(stored)
        };

        Assert.Equal(FilterCodec.Serialize(stored), FilterCodec.Serialize(
            FilterCodec.Deserialize(ProtoFeedController.FilterJson(sub).GetRawText())));
    }

    [Fact]
    public void PreviewRows_ReportTheFailingConditions() {
        var set = FeedFilter.Rows([FeedFilter.Is(ProtoBuildKind.Platform, "ios")]);

        var rows = ProtoFeedController.PreviewRows(FeedEventKinds.Proto, set, null);

        var forward = Assert.Single(rows, r => r.Key == "forward");
        Assert.False(forward.Matches);
        Assert.Contains("Platform", Assert.Single(forward.BlockedBy), StringComparison.Ordinal);
        Assert.Contains(rows, r => r.Key == "broken" && r.Matches);
    }

    [Fact]
    public void MaskWebhook_NonDiscord_GenericTail() {
        string masked = ProtoFeedController.MaskWebhook("https://example.com/hook/SECRETvalue");
        Assert.Equal("...Tvalue", masked);
        Assert.DoesNotContain("SECRETvalue", masked);
    }

    private static ICurrentUser StubUser(string? discordId) => discordId is null
        ? new AnonymousUser()
        : new FakeUser(Guid.Parse("00000000-0000-0000-0000-000000000001"), DiscordId: discordId).Accessor();
}
