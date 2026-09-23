using EggIncognito.Core.Services;

namespace EggIncognito.Tests;

public class ContentRootTests {
    [Fact]
    public void Resolve_PrefersConfiguredPath() {
        using var tmp = new TempDir();
        Directory.CreateDirectory(tmp.Combine("RouteMap"));
        File.WriteAllText(tmp.Combine("RouteMap", "routes.yaml"), "routes:\n");
        Assert.Equal(tmp.Path, ContentRoot.Resolve(tmp.Path));
    }
}
