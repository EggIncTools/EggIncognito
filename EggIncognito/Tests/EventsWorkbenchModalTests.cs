using System.Net;
using System.Text.Json;
using Bunit;
using EggIdentity.Auth;
using EggIncognito.Components.Events;
using EggIncognito.Models.Contracts;
using EggIncognito.Models.Events;
using EggIncognito.Services.Assets;
using EggIncognito.Services.Events;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class EventsWorkbenchModalTests : BunitContext {
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private void Wire(Func<HttpRequestMessage, HttpResponseMessage> respond) {
        Services.AddLogging();
        Services.AddSingleton<IHttpClientFactory>(
            new StubHttpFactory(new StubHttpMessageHandler(respond), "http://localhost/"));
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        Services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
        Services.AddSingleton<ICurrentUser>(new AnonymousUser());
        Services.AddScoped<EventsWorkbenchState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static GameEventDto Event(string id, string message, DateTimeOffset start, DateTimeOffset end, bool ultra = false) =>
        new(id, "earnings-boost", message, 2, ultra, UnixSeconds.FromTime(start), UnixSeconds.FromTime(end), "device");

    private static HttpResponseMessage Ok(params GameEventDto[] events) =>
        StubResponses.Json(HttpStatusCode.OK, new GameEventListResponse(events.Length, events), Web);

    private static HttpResponseMessage NoContracts() =>
        StubResponses.Json(HttpStatusCode.OK, new ContractReleaseListResponse(0, []), Web);

    private static HttpResponseMessage Respond(HttpRequestMessage req, params GameEventDto[] events) =>
        req.RequestUri?.AbsolutePath.StartsWith("/api/v1/contracts", StringComparison.Ordinal) == true
            ? NoContracts()
            : Ok(events);

    private async Task<IRenderedComponent<EventsWorkbenchModal>> OpenAsync() {
        var cut = Render<EventsWorkbenchModal>();
        await cut.InvokeAsync(() => cut.Instance.Open());
        return cut;
    }

    [Fact]
    public async Task Window_RendersOneBarPerEventWithItsMessage() {
        var now = DateTimeOffset.UtcNow;
        Wire(req => Respond(req, Event("a", "Earnings boost", now.AddHours(-2), now.AddHours(2))));

        var cut = await OpenAsync();

        var bars = cut.FindAll(".evcal-bar");
        Assert.NotEmpty(bars);
        Assert.All(bars, bar => Assert.Contains("Earnings boost", bar.TextContent));
    }

    [Fact]
    public async Task OverlappingEvents_StackIntoSeparateLanes() {
        var now = DateTimeOffset.UtcNow;
        Wire(req => Respond(req,
            Event("a", "First", now.AddHours(-2), now.AddHours(2)),
            Event("b", "Second", now.AddHours(-1), now.AddHours(3))));

        var cut = await OpenAsync();

        var currentPeriod = Assert.Single(cut.FindAll(".cal-period"), p => p.QuerySelectorAll(".cal-now").Length > 0);
        var rowsWithBars = currentPeriod.QuerySelectorAll(".cal-row")
            .Where(row => row.QuerySelectorAll(".evcal-bar").Length > 0)
            .ToList();
        Assert.NotEmpty(rowsWithBars);
        Assert.Contains(rowsWithBars, row => row.QuerySelectorAll(".cal-lane").Length == 2);
        Assert.All(rowsWithBars, row => Assert.All(
            row.QuerySelectorAll(".cal-lane"),
            lane => Assert.True(lane.QuerySelectorAll(".evcal-bar").Length <= 1)));
    }

    [Fact]
    public async Task NowLine_IsDrawnOnceWhenNowFallsInsideTheWindow() {
        var now = DateTimeOffset.UtcNow;
        Wire(req => Respond(req, Event("a", "Earnings boost", now.AddHours(-2), now.AddHours(2))));

        var cut = await OpenAsync();

        Assert.Single(cut.FindAll(".cal-now"));
    }

    [Fact]
    public async Task UltraEvent_UsesTheCcGradientAndSprite() {
        var now = DateTimeOffset.UtcNow;
        Wire(req => Respond(req, Event("a", "Ultra sale", now.AddHours(-2), now.AddHours(2), true)));

        var cut = await OpenAsync();

        var style = cut.Find(".evcal-bar").GetAttribute("style") ?? "";
        Assert.Contains(EventPalette.CcGradientFrom, style, StringComparison.Ordinal);
        Assert.Contains(EventPalette.CcGradientTo, style, StringComparison.Ordinal);
        Assert.Contains("cc=1", cut.Find(".evcal-bar-icon").GetAttribute("src") ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task StandardEvent_UsesTheTypeColorAndPlainSprite() {
        var now = DateTimeOffset.UtcNow;
        Wire(req => Respond(req, Event("a", "Earnings boost", now.AddHours(-2), now.AddHours(2))));

        var cut = await OpenAsync();

        var style = cut.Find(".evcal-bar").GetAttribute("style") ?? "";
        Assert.Contains(EventPalette.ColorFor("earnings-boost"), style, StringComparison.Ordinal);
        Assert.DoesNotContain("cc=1", cut.Find(".evcal-bar-icon").GetAttribute("src") ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeRail_SplitsTypesIntoBoostsThenSales() {
        var now = DateTimeOffset.UtcNow;
        Wire(req => Respond(req,
            Event("a", "Earnings boost", now.AddHours(-2), now.AddHours(2)),
            new GameEventDto("b", "hab-sale", "Hab sale", 0.5, false,
                UnixSeconds.FromTime(now.AddHours(-1)), UnixSeconds.FromTime(now.AddHours(3)), "device")));

        var cut = await OpenAsync();

        var groups = cut.FindAll(".evwb-group");
        Assert.Equal(2, groups.Count);
        Assert.Equal(EventPalette.BoostsGroup, groups[0].QuerySelector(".evwb-group-label")?.TextContent);
        Assert.Equal(EventPalette.SalesGroup, groups[1].QuerySelector(".evwb-group-label")?.TextContent);
        Assert.Contains("Earnings Boost", groups[0].TextContent);
        Assert.Contains("Hab Sale", groups[1].TextContent);
        Assert.DoesNotContain("Hab Sale", groups[0].TextContent);
    }

    [Theory]
    [InlineData("drone-boost", EventPalette.BoostsGroup)]
    [InlineData("earnings-boost", EventPalette.BoostsGroup)]
    [InlineData("gift-boost", EventPalette.BoostsGroup)]
    [InlineData("piggy-boost", EventPalette.BoostsGroup)]
    [InlineData("prestige-boost", EventPalette.BoostsGroup)]
    [InlineData("boost-duration", EventPalette.BoostsGroup)]
    [InlineData("mission-capacity", EventPalette.BoostsGroup)]
    [InlineData("mission-fuel", EventPalette.BoostsGroup)]
    [InlineData("crafting-sale", EventPalette.SalesGroup)]
    [InlineData("epic-research-sale", EventPalette.SalesGroup)]
    [InlineData("hab-sale", EventPalette.SalesGroup)]
    [InlineData("research-sale", EventPalette.SalesGroup)]
    [InlineData("vehicle-sale", EventPalette.SalesGroup)]
    public void GroupOf_SortsEveryKnownTypeIntoBoostsOrSales(string type, string expected) {
        Assert.Equal(expected, EventPalette.GroupOf(type));
    }

    [Fact]
    public async Task DatabaseLessInstance_ShowsAShortNoteAndNoBars() {
        Wire(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var cut = await OpenAsync();

        Assert.Contains("needs a database", cut.Find(".evcal-note").TextContent);
        Assert.Empty(cut.FindAll(".evcal-bar"));
    }

}
