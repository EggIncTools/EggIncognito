using EggIncognito.Core.Services.Devices;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class ProtoVersionQualityTests {
    [Theory]
    [InlineData("111342", true)]
    [InlineData("72", true)]
    [InlineData("1.35.7.1", false)]
    [InlineData("a1b2c3d4e5f6g7h8", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAndroidStyleBuild_DetectsBareIntegers(string? build, bool expected) =>
        Assert.Equal(expected, ProtoVersionQuality.IsAndroidStyleBuild(build));

    [Fact]
    public void Mismatch_IosWithIntegerBuild_IsFlagged() {
        Assert.True(ProtoVersionQuality.HasPlatformBuildMismatch("ios", "111342"));
        Assert.Equal("android_build_on_ios", ProtoVersionQuality.BuildQualityFlag("ios", "111342"));
    }

    [Fact]
    public void Mismatch_IosWithDottedOrHashBuild_IsClean() {
        Assert.False(ProtoVersionQuality.HasPlatformBuildMismatch("ios", "1.35.7.1"));
        Assert.False(ProtoVersionQuality.HasPlatformBuildMismatch("ios", "a1b2c3d4e5f6g7h8"));
        Assert.Null(ProtoVersionQuality.BuildQualityFlag("ios", "1.35.7.1"));
    }

    [Fact]
    public void Mismatch_AndroidWithIntegerBuild_IsClean() {
        Assert.False(ProtoVersionQuality.HasPlatformBuildMismatch("android", "111342"));
        Assert.Null(ProtoVersionQuality.BuildQualityFlag("android", "111342"));
    }

    [Fact]
    public void Mismatch_IsCaseInsensitiveOnPlatform() {
        Assert.True(ProtoVersionQuality.HasPlatformBuildMismatch("iOS", "111342"));
        Assert.True(ProtoVersionQuality.HasPlatformBuildMismatch("IOS", "111342"));
    }

    [Fact]
    public void Ordering_Android_RanksByVersionCode() {
        VersionKey older = new("android", "1.35.6", "111341", null, null, null);
        VersionKey newer = new("android", "1.35.7", "111342", null, null, null);
        Assert.True(ProtoVersionOrdering.Compare(newer, older) < 0);
    }

    [Fact]
    public void Ordering_Ios_SameAppVersion_RebuildIsNotARepeat() {
        VersionKey first = new("ios", "1.35.7", "1.35.7.1", null, null, null);
        VersionKey rebuild = new("ios", "1.35.7", "1.35.7.2", null, null, null);
        Assert.NotEqual(0, ProtoVersionOrdering.Compare(rebuild, first));
        Assert.True(ProtoVersionOrdering.Compare(rebuild, first) < 0);
    }

    [Fact]
    public void Ordering_Ios_NewerDottedWins() {
        VersionKey v1357 = new("ios", "1.35.7", "1.35.7.1", null, null, null);
        VersionKey v1358 = new("ios", "1.35.8", "1.35.8.0", null, null, null);
        Assert.True(ProtoVersionOrdering.Compare(v1358, v1357) < 0);
    }

    [Fact]
    public void VersionCompare_OrdersComponentsAndInvalidsLowest() {
        Assert.True(VersionCompare.Dotted("1.36.0", "1.35.9") > 0);
        Assert.True(VersionCompare.Dotted("2.0.0", "1.99.99") > 0);
        Assert.Equal(0, VersionCompare.Dotted("1.2", "1.2.0"));
        Assert.True(VersionCompare.Dotted("", "0.0.1") < 0);
        Assert.Equal(0, VersionCompare.Dotted(null, ""));
        Assert.Equal(long.MinValue, VersionCompare.DottedKey(""));
    }

    [Fact]
    public void VersionCompare_IsTransitiveAcrossMixedBuilds() {
        string?[] values = ["111342", "1.35.7.1", null, "abc", "111341"];
        var sorted = values.OrderBy(v => v, VersionCompare.BuildComparer).ToList();
        for (int i = 0; i < sorted.Count; i++) {
            for (int j = i + 1; j < sorted.Count; j++) Assert.True(VersionCompare.Build(sorted[i], sorted[j]) <= 0);
        }
    }
}
