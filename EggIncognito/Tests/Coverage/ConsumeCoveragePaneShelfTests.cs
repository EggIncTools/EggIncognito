using Bunit;
using EggIdentity.Auth;
using EggIdentity.Contract;
using EggIdentity.UI;
using EggIncognito.Components.Api;
using EggIncognito.Core.Services.Devices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests.Coverage;

public class ConsumeCoveragePaneShelfTests : BunitContext {
    private void Wire(params IDevicePanel[] panels) {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddHttpClient();
        Services.AddEggIdentityToasts();
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        Services.AddSingleton<ICurrentUser>(new FakeUser(Guid.NewGuid(), UserRole.Admin, DiscordId: "tester").Accessor());
        foreach (var panel in panels) Services.AddSingleton(panel);
    }

    [Fact]
    public void HeadShelf_RendersRegisteredPanelsWithTheDeviceContext() {
        Wire(new DevicePanel("head", "Head", DevicePanelShelves.CoverageHead, typeof(Probe)));

        var cut = Render<ConsumeCoveragePane>(p => p.Add(x => x.DeviceId, "dev-1").Add(x => x.AndroidUserId, 10));

        var probe = cut.Find(".probe");
        Assert.Equal("DevicePanelContext:dev-1", probe.TextContent);
    }

    [Fact]
    public void NoPanels_RendersNoSideOrDropsColumns() {
        Wire();

        var cut = Render<ConsumeCoveragePane>();

        Assert.Empty(cut.FindAll(".probe"));
        Assert.Empty(cut.FindAll(".cov-side"));
        Assert.Empty(cut.FindAll(".cov-drops"));
    }

    [Fact]
    public void SideAndDropsShelves_StayHiddenWithoutACoverageMap() {
        Wire(new DevicePanel("side", "Side", DevicePanelShelves.CoverageSide, typeof(Probe)),
            new DevicePanel("drops", "Drops", DevicePanelShelves.CoverageDrops, typeof(Probe)));

        var cut = Render<ConsumeCoveragePane>();

        Assert.Empty(cut.FindAll(".cov-side"));
        Assert.Empty(cut.FindAll(".cov-drops"));
    }

    public sealed class Probe : ComponentBase {
        [Parameter] public object? Context { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder) {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", "probe");
            builder.AddContent(2, Context switch {
                DevicePanelContext d => $"{nameof(DevicePanelContext)}:{d.DeviceId}",
                CoverageCellContext c => $"{nameof(CoverageCellContext)}:{c.Family}",
                _ => "none"
            });
            builder.CloseElement();
        }
    }
}
