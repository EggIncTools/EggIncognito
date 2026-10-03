using EggIdentity.Auth;
using EggIdentity.Contract;
using EggIncognito.Services;
using EggIncognito.Services.Auth;

namespace EggIncognito.Tests.Auth;

public class MockAccessGuardTests {
    private sealed class FakeMode(AppMode mode) : IAppMode {
        public AppMode Mode => mode;
        public bool CanCapture => false;
        public bool CanWrite => false;
    }

    private static ICurrentUser User(UserRole role) => new FakeUser(Guid.NewGuid(), role).Accessor();

    [Fact]
    public void HostedNonAdmin_Blocked() {
        Assert.True(MockAccessGuard.Blocks("ei_afx/zoom_zoom", new FakeMode(AppMode.Hosted),
            User(UserRole.Contributor)));
    }

    [Fact]
    public void HostedAdmin_Allowed() {
        Assert.False(MockAccessGuard.Blocks("ei_afx/zoom_zoom", new FakeMode(AppMode.Hosted),
            User(UserRole.Admin)));
    }

    [Fact]
    public void Local_Allowed() {
        Assert.False(MockAccessGuard.Blocks("ei_afx/zoom_zoom", new FakeMode(AppMode.Local),
            User(UserRole.Viewer)));
    }

    [Fact]
    public void UnlistedPath_Allowed() {
        Assert.False(MockAccessGuard.Blocks("ei_afx/config", new FakeMode(AppMode.Hosted),
            User(UserRole.Viewer)));
    }
}
