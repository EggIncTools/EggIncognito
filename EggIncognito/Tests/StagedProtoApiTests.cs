using EggIncognito.Data.Models;
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

    private static ProtoVersion Android177() => new() {
        Platform = "android",
        AppVersion = "1.7.7",
        Build = "111079",
        ClientVersion = "15",
        ProtoSha = "eee8a15173a3"
    };

    [Fact]
    public void Evaluate_ShaOnOtherPlatform_IsOfferableNotConflict() {
        var r = StagedProtoStore.Evaluate([Android177()], false, "ios", "1.7.7", "1.7.7.0", "15");

        Assert.False(r.InRegistry);
        Assert.False(r.KnownCombination);
        Assert.False(r.Conflict);
        Assert.Null(r.Stored);
    }

    [Fact]
    public void Evaluate_SamePlatformDifferentMetadata_IsConflictWithStored() {
        var r = StagedProtoStore.Evaluate([Android177()], false, "android", "1.7.8", "111080", "15");

        Assert.True(r.InRegistry);
        Assert.False(r.KnownCombination);
        Assert.True(r.Conflict);
        Assert.Equal("111079", r.Stored?.Build);
    }

    [Fact]
    public void Evaluate_SamePlatformCompatible_IsKnown() {
        var r = StagedProtoStore.Evaluate([Android177()], false, "ANDROID", "1.7.7", "", null);

        Assert.True(r.InRegistry);
        Assert.True(r.KnownCombination);
        Assert.False(r.Conflict);
    }

    [Fact]
    public void OnPlatform_PartitionsRowsBySha() {
        var ios = new ProtoVersion { Platform = "ios", Build = "1.7.7.0", ProtoSha = "eee8a15173a3" };

        Assert.Same(ios, Assert.Single(StagedProtoStore.OnPlatform([Android177(), ios], "IOS")));
        Assert.Equal(2, StagedProtoStore.OnPlatform([Android177(), ios], null).Count);
    }
}
