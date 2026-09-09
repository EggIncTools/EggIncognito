using Bunit;
using EggIncognito.Components.Inspector;
using EggIncognito.Core.Services;
using EggIncognito.Models.Inspector;
using EggIncognito.Services.Api;
using EggIncognito.Services.Inspector;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class InspectorReaderTests : BunitContext {
    private static readonly HttpClient Http = new();

    private static SendResponse Ok() => new(200, null, null, null, null, null);

    private static DiagnoseDto Broken() => new(false, 4, 1, null, null, null, null, null, null);

    private static List<TransportStage> Stages() => [];

    private IRenderedComponent<TransactionView> RenderView() {
        Services.AddLogging();
        return Render<TransactionView>(p => p
            .Add(c => c.ClientFactory, () => Http)
            .Add(c => c.Build, Stages()));
    }

    private static bool Collapsed(IRenderedComponent<TransactionView> cut, int index) =>
        (cut.FindAll(".insp-disc")[index].GetAttribute("class") ?? "")
        .Contains("collapsed", StringComparison.Ordinal);

    [Fact]
    public void Sent_IsOpenBeforeAResponseArrives() {
        var cut = RenderView();
        Assert.False(Collapsed(cut, 0));
    }

    [Fact]
    public void Sent_CollapsesOnceWhenTheResponseArrives() {
        var cut = RenderView();
        cut.Render(p => p.Add(c => c.Response, Ok()));
        Assert.True(Collapsed(cut, 0));
    }

    [Fact]
    public void Sent_StaysOpenWhenTheUserToggledItSinceTheLastBuild() {
        var cut = RenderView();
        cut.FindAll("button.insp-disc-toggle")[0].Click();
        cut.FindAll("button.insp-disc-toggle")[0].Click();
        cut.Render(p => p.Add(c => c.Response, Ok()));
        Assert.False(Collapsed(cut, 0));
    }

    [Fact]
    public void ANewBuild_ReopensSent() {
        var cut = RenderView();
        cut.Render(p => p.Add(c => c.Response, Ok()));
        cut.Render(p => p.Add(c => c.Build, Stages()));
        Assert.False(Collapsed(cut, 0));
    }

    [Fact]
    public void Diagnosis_IsAbsentUntilAFailedDecodeThenOpens() {
        var cut = RenderView();
        Assert.Single(cut.FindAll(".insp-disc"));
        cut.Render(p => p.Add(c => c.Diagnosis, Broken()));
        Assert.Equal(2, cut.FindAll(".insp-disc").Count);
        Assert.False(Collapsed(cut, 1));
    }

    [Fact]
    public void TransactionView_OwnsNoModeControl() {
        var cut = RenderView();
        cut.Render(p => p.Add(c => c.Response, Ok()));
        cut.Render(p => p.Add(c => c.Diagnosis, Broken()));
        Assert.Empty(cut.FindAll(".wb-seg"));
    }

    [Fact]
    public void EnvValidation_SurvivesATransactionClear() {
        var state = new ApiWorkbenchState {
            EnvValidated = true,
            EnvOpen = false,
            LastBuild = new BuildResponse(null, "", "", false, null, null, null),
            Response = Ok(),
            Diagnosis = Broken()
        };

        state.ClearTransaction();
        Assert.True(state.EnvValidated);
        Assert.False(state.EnvOpen);
    }

    [Fact]
    public void TargetResetsToMockOnAFreshState() => Assert.Equal(InspectorTarget.Mock, new ApiWorkbenchState().Target);
}
