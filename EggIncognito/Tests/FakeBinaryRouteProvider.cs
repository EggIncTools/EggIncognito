using EggIncognito.Core.Services;

namespace EggIncognito.Tests;

internal sealed class FakeBinaryRouteProvider(params BinaryRouteInfo[] routes) : IBinaryRouteProvider {
    public BinaryRouteInfo? GetBinaryRoute(string path) => routes.FirstOrDefault(r => r.Path == path);
    public IReadOnlyList<BinaryRouteInfo> AllBinaryRoutes() => routes;

    public void Invalidate() {
    }
}
