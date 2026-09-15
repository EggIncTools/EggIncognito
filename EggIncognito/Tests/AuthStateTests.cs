using EggIncognito.Services;

namespace EggIncognito.Tests;

public class AuthStateTests {
    [Fact]
    public void WidgetEnabled_False_WhenNoIdentityHostUrl() {
        var state = new AuthState(true, SessionActive: true);
        Assert.False(state.WidgetEnabled);
        Assert.False(state.Enabled);
    }

    [Fact]
    public void WidgetEnabled_False_WhenIdentityApiOff() {
        var state = new AuthState(false, "http://identity.local", SessionActive: true);
        Assert.False(state.WidgetEnabled);
    }

    [Fact]
    public void WidgetEnabled_False_WhenNoSharedSessionSecret() {
        var state = new AuthState(true, "http://identity.local");
        Assert.False(state.WidgetEnabled);
        Assert.False(state.Enabled);
    }

    [Fact]
    public void WidgetEnabled_True_WhenApiHostUrlAndSessionPresent() {
        var state = new AuthState(true, "http://identity.local", SessionActive: true);
        Assert.True(state.WidgetEnabled);
        Assert.True(state.Enabled);
    }
}
