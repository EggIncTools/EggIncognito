using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class Arm64AddrRefResolverTests {
    [Fact]
    public void FindReferrers_NullOrShort_Empty() {
        Assert.Empty(Arm64AddrRefResolver.FindReferrers(null!, 0x1000));
        Assert.Empty(Arm64AddrRefResolver.FindReferrers(new MachoImage(new byte[16]), 0x1000));
    }

    [Fact]
    public void FindReferrers_NotAMacho_Empty() {
        byte[] junk = new byte[256];
        for (int i = 0; i < junk.Length; i++) junk[i] = (byte)(i * 7);
        Assert.Empty(Arm64AddrRefResolver.FindReferrers(new MachoImage(junk), 0x1000));
    }

    [Fact]
    public void FindReferrers_MachoWithoutFunctionStarts_Empty() {
        byte[] text = new byte[64];
        byte[] bin = SyntheticMacho.Build(text, []);
        Assert.Empty(Arm64AddrRefResolver.FindReferrers(new MachoImage(bin), SyntheticMacho.TextVm));
    }
}
