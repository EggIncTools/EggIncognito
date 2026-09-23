using Bunit;
using EggIncognito.Components.Inspector;
using EggIncognito.Services.Api;
using EggIncognito.Services.Inspector;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class EnvironmentPanelTests : BunitContext {
    private readonly ApiWorkbenchState _state = new();

    private IRenderedComponent<EnvironmentPanel> RenderPanel() {
        Services.AddSingleton(_state);
        _state.SeedEnvRows();
        return Render<EnvironmentPanel>(p => p.Add(c => c.Rows, _state.EnvRows));
    }

    [Fact]
    public void Open_ShowsTheValidateButton() {
        var cut = RenderPanel();
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Validate"));
    }

    [Fact]
    public void Collapsed_ShowsValidationStateAndHidesTheRows() {
        _state.EnvOpen = false;
        _state.EnvValidated = true;
        var cut = RenderPanel();
        Assert.Contains("validated", cut.Markup);
        Assert.Empty(cut.FindAll(".env-rowgroup"));
    }

    [Fact]
    public void FailedValidation_OffersForceSave() {
        _state.EnvError = "clientVersion does not parse";
        var cut = RenderPanel();
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Save anyway"));
    }

    [Fact]
    public void HeaderClick_TogglesTheSection() {
        var cut = RenderPanel();
        cut.Find("button.env-title").Click();
        Assert.False(_state.EnvOpen);
        Assert.Empty(cut.FindAll(".env-rowgroup"));
        cut.Find("button.env-title").Click();
        Assert.True(_state.EnvOpen);
    }

    [Theory]
    [InlineData("111358", false)]
    [InlineData("1.37.0.1", false)]
    [InlineData("build7", true)]
    [InlineData("", false)]
    public void BuildRow_AcceptsPlainAndDottedBuilds(string value, bool invalid) {
        var row = new EnvRow {
            Key = "build",
            ValueType = EnvValueType.String,
            Editor = EnvEditor.Build,
            Value = value
        };
        Assert.Equal(invalid, row.IsInvalid());
    }
}
