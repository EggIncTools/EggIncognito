using System.Net;
using System.Text.Json;
using Bunit;
using EggIdentity.Contract;
using EggIncognito.Components.Contracts;
using EggIncognito.Models.Contracts;
using EggIncognito.Services;
using EggIncognito.Services.Contracts;
using EggIncognito.Services.Events;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class ContractsWorkbenchModalTests : BunitContext {
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private void Wire(Func<HttpRequestMessage, HttpResponseMessage> respond) {
        Services.AddLogging();
        Services.AddSingleton<IHttpClientFactory>(
            new StubHttpFactory(new StubHttpMessageHandler(respond), new Uri("http://localhost")));
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        Services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
        Services.AddSingleton<ICurrentUser>(new FakeUser(authenticated: false, role: UserRole.Viewer, discordId: null));
        Services.AddScoped<ContractsWorkbenchState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static ContractReleaseDto Release(
        string id, string name, DateTimeOffset start, DateTimeOffset end, bool leggacy = false, int pe = 0, bool ultra = false) =>
        new(1, id, name, 1, null, null, UnixSeconds.FromTime(start), UnixSeconds.FromTime(end),
            (end - start).TotalSeconds, leggacy, ultra, pe, true, 10, "device");

    private static HttpResponseMessage Ok(params ContractReleaseDto[] releases) =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK,
            JsonSerializer.Serialize(new ContractReleaseListResponse(releases.Length, releases), Web));

    private async Task<IRenderedComponent<ContractsWorkbenchModal>> OpenAsync() {
        var cut = Render<ContractsWorkbenchModal>();
        await cut.InvokeAsync(() => cut.Instance.Open());
        return cut;
    }

    [Fact]
    public async Task Window_RendersOneBarPerReleaseWithItsName() {
        var now = DateTimeOffset.UtcNow;
        Wire(_ => Ok(Release("a", "Hell Week", now.AddDays(-1), now.AddDays(3))));

        var cut = await OpenAsync();

        var bars = cut.FindAll(".evcal-bar");
        Assert.NotEmpty(bars);
        Assert.All(bars, bar => Assert.Contains("Hell Week", bar.TextContent));
        Assert.All(bars, bar => Assert.Contains("evcal-bar-contract", bar.ClassName));
    }

    [Fact]
    public async Task KindFilter_HidesLeggacyReleasesWhenToggledOff() {
        var now = DateTimeOffset.UtcNow;
        Wire(_ => Ok(
            Release("a", "Fresh", now.AddDays(-1), now.AddDays(3)),
            Release("b", "Old Favourite", now.AddDays(-1), now.AddDays(3), leggacy: true)));

        var cut = await OpenAsync();
        Assert.Contains(cut.FindAll(".evcal-bar"), b => b.TextContent.Contains("Old Favourite"));

        await cut.InvokeAsync(() => cut.FindAll(".evwb-row").First(r => r.TextContent.Trim() == "Leggacy").Click());

        Assert.DoesNotContain(cut.FindAll(".evcal-bar"), b => b.TextContent.Contains("Old Favourite"));
        Assert.Contains(cut.FindAll(".evcal-bar"), b => b.TextContent.Contains("Fresh"));
    }

    [Fact]
    public async Task DatabaseLessInstance_ShowsAShortNoteAndNoBars() {
        Wire(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var cut = await OpenAsync();

        Assert.Contains("needs a database", cut.Find(".evcal-note").TextContent);
        Assert.Empty(cut.FindAll(".evcal-bar"));
    }
}
