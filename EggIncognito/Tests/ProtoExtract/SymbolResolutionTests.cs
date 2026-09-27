using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class SymbolResolutionTests {
    [Fact]
    public void IsLocalEntity_ClassifiesManglings() {
        Assert.True(MachoSymbols.IsLocalEntity("__ZZN9FarmScene16updateTrophyCaseEP14GameControllerENK3$_1clEv"));
        Assert.True(MachoSymbols.IsLocalEntity("_some_method_block_invoke"));
        Assert.False(MachoSymbols.IsLocalEntity("__ZN9FarmScene16updateTrophyCaseEP14GameController"));
        Assert.False(MachoSymbols.IsLocalEntity(""));
    }

    [Fact]
    public void FieldWriteScanner_RejectsAnEmptyBinary() {
        var r = Arm64FieldWriteScanner.Scan([], 0, 0x10);
        Assert.False(r.Ok);
        Assert.Empty(r.Writes);
    }
}
