using System.Collections.Frozen;
using EggIdentity.Auth;
using EggIdentity.Contract;

namespace EggIncognito.Services.Auth;

public static class MockAccessGuard {
    public static readonly FrozenSet<string> AdminOnlyHosted =
        FrozenSet.Create(StringComparer.Ordinal, "ei_afx/zoom_zoom");

    public static bool Blocks(string path, IAppMode mode, ICurrentUser user) =>
        AdminOnlyHosted.Contains(path) && mode.Mode == AppMode.Hosted && !user.Current.IsAtLeast(UserRole.Admin);
}
