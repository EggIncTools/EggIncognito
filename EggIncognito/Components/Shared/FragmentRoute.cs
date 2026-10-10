using EggIdentity.UI;
using Microsoft.AspNetCore.Components;

namespace EggIncognito.Components.Shared;

public static class FragmentRoute {
    public static void WriteFragment(this PathRouteSync sync, NavigationManager nav, string hash, bool push) {
        var path = nav.ToBaseRelativePath(nav.Uri).Split('#')[0];
        var target = hash.TrimStart('#') is { Length: > 0 } h ? $"{path}#{h}" : path;
        if (push) sync.Push(target);
        else sync.Replace(target);
    }
}
