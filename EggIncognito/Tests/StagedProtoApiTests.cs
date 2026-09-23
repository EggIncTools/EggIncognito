using EggIncognito.Data.Services;
using EggIncognito.Services.Protos;

namespace EggIncognito.Tests;

public class StagedProtoApiTests {
    [Fact]
    public void OfferResult_EnumNames_LowercaseForJson() {
        Assert.Equal("staged", StagedProtoStore.OfferResult.Staged.ToString().ToLowerInvariant());
        Assert.Equal("alreadyinregistry", StagedProtoStore.OfferResult.AlreadyInRegistry.ToString().ToLowerInvariant());
    }

    [Theory]
    [InlineData("ios", "ios", true)]
    [InlineData("ios", "IOS", true)]
    [InlineData("ios", "android", false)]
    [InlineData("", "1.37", true)]
    [InlineData("1.37", "", true)]
    [InlineData("  1.37  ", "1.37", true)]
    [InlineData(null, "75", true)]
    [InlineData("75", "76", false)]
    public void FieldCompatible_TreatsMissingAsWildcard(string? a, string? b, bool expected) =>
        Assert.Equal(expected, StagedProtoStore.FieldCompatible(a, b));

    [Fact]
    public void GroupStatus_InRegistry_IsNotOfferable() {
        Assert.False(new GroupStatus(false, false, false, false, true).Offerable);
        Assert.False(new GroupStatus(true, false, false).Offerable);
        Assert.False(new GroupStatus(false, true, false).Offerable);
        Assert.False(new GroupStatus(false, false, false, true).Offerable);
        Assert.False(new GroupStatus(false, false, false, false, false, true).Offerable);
        Assert.True(new GroupStatus(false, false, false).Offerable);
    }
}
