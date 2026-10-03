using EggIncognito.Core.Services;

namespace EggIncognito.Tests;

public class ContentRootTests {
    [Fact]
    public void Resolve_PrefersConfiguredPath() {
        using var tmp = new TempDir();
        Directory.CreateDirectory(tmp.File("RouteMap"));
        File.WriteAllText(tmp.File("RouteMap", "routes.yaml"), "routes:\n");
        Assert.Equal(tmp.Path, ContentRoot.Resolve(tmp.Path));
    }
}
