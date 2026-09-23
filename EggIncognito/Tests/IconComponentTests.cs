using EggIdentity.Icons;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests;

public class IconComponentTests {
    [Fact]
    public void PlatformIcons_ResolveToPackNames() {
        Assert.Contains(PlatformIcons.For("ios"), IconPack.Names);
        Assert.Contains(PlatformIcons.For("android"), IconPack.Names);
        Assert.Contains(PlatformIcons.For(null), IconPack.Names);
    }
}
