using EggIdentity.UI;

namespace EggIncognito.Components;

public static class WorkbenchRoute {
    public static void WriteRootFragment(this PathRouteSync? sync, string fragment, bool push) {
        if (sync is null) return;
        var url = fragment.Length == 0 ? "/" : "/#" + string.Join('/', fragment.Split('/').Select(Uri.EscapeDataString));
        if (push) sync.Push(url);
        else sync.Replace(url);
    }
}
