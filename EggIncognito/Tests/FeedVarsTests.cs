using EggIncognito.Services.Feed;

namespace EggIncognito.Tests;

public class FeedVarsTests {
    [Fact]
    public void EveryKindVar_KeepsItsNameAndOrder() {
        foreach (var kind in FeedEventKinds.All) Assert.Equal(kind.Vars, FeedVars.Describe(kind, []).Select(v => v.Name));
    }

    [Fact]
    public void EveryFixtureEvent_FillsEveryDeclaredVar() {
        foreach (var evt in FeedFixtures.All()) {
            var kind = FeedEventKinds.For(evt);
            Assert.Equal(kind.Vars.Order(StringComparer.Ordinal), evt.Vars().Keys.Order(StringComparer.Ordinal));
        }
    }

    [Theory]
    [InlineData("platform", "Platform")]
    [InlineData("appVersion", "App version")]
    [InlineData("prevAppVersion", "Prev app version")]
    [InlineData("sha", "Sha")]
    public void Label_SplitsCamelCase(string name, string expected) => Assert.Equal(expected, FeedVars.Label(name));
}
