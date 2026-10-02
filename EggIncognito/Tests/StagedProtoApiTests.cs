using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Protos;

namespace EggIncognito.Tests;

public class StagedProtoApiTests {
    [Fact]
    public void OfferResult_EnumNames_LowercaseForJson() {
        Assert.Equal("staged", StagedProtoStore.OfferResult.Staged.ToString().ToLowerInvariant());
        Assert.Equal("alreadyinregistry", StagedProtoStore.OfferResult.AlreadyInRegistry.ToString().ToLowerInvariant());
        Assert.Equal("archiveflagged", StagedProtoStore.OfferResult.ArchiveFlagged.ToString().ToLowerInvariant());
        Assert.Equal("alreadyarchived", StagedProtoStore.OfferResult.AlreadyArchived.ToString().ToLowerInvariant());
    }

    [Fact]
    public void GroupStatus_KnownUnflagged_IsFlaggableNotOfferable() {
        var unflagged = new GroupStatus(true, false, false, false, true);
        var archived = unflagged with { Archived = true };

        Assert.True(unflagged.Flaggable);
        Assert.False(unflagged.Offerable);
        Assert.False(archived.Flaggable);
        Assert.False(archived.Offerable);
        Assert.False((unflagged with { Pending = true }).Flaggable);
        Assert.False(new GroupStatus(false, false, false).Flaggable);
    }

    [Fact]
    public void Evaluate_ReportsArchivedOnlyWhenEveryCompatibleRowIsFlagged() {
        var flagged = Android177();
        flagged.ArchiveSourced = true;

        Assert.False(StagedProtoStore.Evaluate([Android177()], false, "android", "1.7.7", "111079", "15").Archived);
        Assert.True(StagedProtoStore.Evaluate([flagged], false, "android", "1.7.7", "111079", "15").Archived);
        Assert.False(StagedProtoStore.Evaluate([flagged, Android177()], false, "android", "1.7.7", null, null).Archived);
        Assert.False(StagedProtoStore.Evaluate([flagged], false, "ios", "1.7.7", null, null).Archived);
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
    public void GroupStatus_NewBuildOfKnownSha_IsOfferable() {
        Assert.True(new GroupStatus(false, false, false, false, true).Offerable);
        Assert.False(new GroupStatus(true, false, false).Offerable);
        Assert.False(new GroupStatus(false, true, false).Offerable);
        Assert.False(new GroupStatus(false, false, false, true).Offerable);
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
    public void Evaluate_ShaOnOtherPlatform_IsOfferable() {
        var r = StagedProtoStore.Evaluate([Android177()], false, "ios", "1.7.7", "1.7.7.0", "15");

        Assert.False(r.InRegistry);
        Assert.False(r.KnownCombination);
    }

    [Fact]
    public void Evaluate_SamePlatformDifferentMetadata_IsNewVersionOfSameSha() {
        var r = StagedProtoStore.Evaluate([Android177()], false, "android", "1.7.8", "111080", "15");

        Assert.True(r.InRegistry);
        Assert.False(r.KnownCombination);
    }

    [Fact]
    public void Evaluate_SamePlatformCompatible_IsKnown() {
        var r = StagedProtoStore.Evaluate([Android177()], false, "ANDROID", "1.7.7", "", null);

        Assert.True(r.InRegistry);
        Assert.True(r.KnownCombination);
    }

    [Fact]
    public void Evaluate_BuildRowWithOtherSha_IsKnownAndShaDiffers() {
        var farm = Android177();
        farm.ProtoSha = "9a9ffabc5868";

        var r = StagedProtoStore.Evaluate([], false, "android", "1.7.7", "111079", "15", [farm], "4f4397fc54e2");

        Assert.False(r.InRegistry);
        Assert.True(r.KnownCombination);
        Assert.False(r.Archived);
        Assert.True(r.ShaDiffers);
        Assert.True(new GroupStatus(r.KnownCombination, r.Pending, false, false, r.InRegistry, r.Archived, r.ShaDiffers).Flaggable);
    }

    [Fact]
    public void Evaluate_ArchivedBuildRowWithOtherSha_StaysFlaggable() {
        var archived = Android177();
        archived.ArchiveSourced = true;

        var r = StagedProtoStore.Evaluate([], false, "android", "1.7.7", "111079", "15", [archived], "4f4397fc54e2");

        Assert.True(r.Archived);
        Assert.True(r.ShaDiffers);
        Assert.True(new GroupStatus(r.KnownCombination, false, false, false, false, r.Archived, r.ShaDiffers).Flaggable);
        Assert.False(new GroupStatus(r.KnownCombination, false, false, false, false, r.Archived).Flaggable);
    }

    [Fact]
    public void Evaluate_ArchivedBuildRowWithOtherClientVersion_IsMetaDiffersAndFlaggable() {
        var stale = Android177();
        stale.ArchiveSourced = true;
        stale.ClientVersion = "14";

        var r = StagedProtoStore.Evaluate([stale], false, "android", "1.7.7", "111079", "15", [stale], "eee8a15173a3");

        Assert.True(r.KnownCombination);
        Assert.True(r.Archived);
        Assert.False(r.ShaDiffers);
        Assert.True(r.MetaDiffers);
        Assert.True(new GroupStatus(r.KnownCombination, false, false, false, r.InRegistry, r.Archived, r.ShaDiffers,
            MetaDiffers: r.MetaDiffers).Flaggable);
    }

    [Fact]
    public void Evaluate_ArchivedBuildRowWithOtherAppVersion_IsMetaDiffers() {
        var stale = Android177();
        stale.ArchiveSourced = true;

        var r = StagedProtoStore.Evaluate([stale], false, "android", "1.7.8", "111079", "15", [stale], "eee8a15173a3");

        Assert.True(r.MetaDiffers);
    }

    [Fact]
    public void Evaluate_BlankExtractedMetadata_DoesNotDiffer() {
        var archived = Android177();
        archived.ArchiveSourced = true;

        var r = StagedProtoStore.Evaluate([archived], false, "android", "", "111079", null, [archived], "eee8a15173a3");

        Assert.False(r.MetaDiffers);
        Assert.False(new GroupStatus(r.KnownCombination, false, false, false, r.InRegistry, r.Archived, r.ShaDiffers,
            MetaDiffers: r.MetaDiffers).Flaggable);
    }

    [Fact]
    public void OfferResult_PublishedAndRejected_LowercaseForJson() {
        Assert.Equal("published", StagedProtoStore.OfferResult.Published.ToString().ToLowerInvariant());
        Assert.Equal("rejected", StagedProtoStore.OfferResult.Rejected.ToString().ToLowerInvariant());
        Assert.False(new GroupStatus(false, false, false, Rejected: true).Offerable);
    }

    [Fact]
    public void OnPlatform_PartitionsRowsBySha() {
        var ios = new ProtoVersion { Platform = "ios", Build = "1.7.7.0", ProtoSha = "eee8a15173a3" };

        Assert.Same(ios, Assert.Single(StagedProtoStore.OnPlatform([Android177(), ios], "IOS")));
        Assert.Equal(2, StagedProtoStore.OnPlatform([Android177(), ios], null).Count);
    }
}
