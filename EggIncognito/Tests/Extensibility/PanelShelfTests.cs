using Bunit;
using EggIncognito.Components.Shared;
using EggIncognito.Core.Services.Devices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests.Extensibility;

public class PanelShelfTests : BunitContext {
    private const string Shelf = "test-shelf";

    private void Register(params IDevicePanel[] panels) {
        foreach (var panel in panels) Services.AddSingleton(panel);
    }

    [Fact]
    public void PanelsOnTheShelf_RenderInRegistrationOrderWithTheContext() {
        Register(new DevicePanel("a", "A", Shelf, typeof(PanelA)), new DevicePanel("b", "B", Shelf, typeof(PanelB)));
        var context = new DevicePanelContext("dev-1", 10);

        var cut = Render<PanelShelf>(p => p.Add(x => x.Shelf, Shelf).Add(x => x.Context, context));

        var marks = cut.FindAll(".mark").Select(e => e.TextContent).ToList();
        Assert.Equal(new[] { "A:dev-1", "B:dev-1" }, marks);
    }

    [Fact]
    public void PanelOnAnotherShelf_IsNotRendered() {
        Register(new DevicePanel("a", "A", Shelf, typeof(PanelA)), new DevicePanel("b", "B", "other", typeof(PanelB)));

        var cut = Render<PanelShelf>(p => p.Add(x => x.Shelf, Shelf).Add(x => x.Context, new DevicePanelContext(null, null)));

        Assert.Single(cut.FindAll(".mark"));
    }

    [Fact]
    public void NoPanels_RendersNothing() {
        var cut = Render<PanelShelf>(p => p.Add(x => x.Shelf, Shelf));
        Assert.Equal("", cut.Markup.Trim());
    }

    public abstract class MarkPanel : ComponentBase {
        [Parameter] public object? Context { get; set; }

        protected abstract string Label { get; }

        protected override void BuildRenderTree(RenderTreeBuilder builder) {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", "mark");
            builder.AddContent(2, $"{Label}:{(Context as DevicePanelContext)?.DeviceId}");
            builder.CloseElement();
        }
    }

    public sealed class PanelA : MarkPanel {
        protected override string Label => "A";
    }

    public sealed class PanelB : MarkPanel {
        protected override string Label => "B";
    }
}
