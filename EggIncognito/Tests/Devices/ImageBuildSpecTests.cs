using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class ImageBuildSpecTests {
    [Fact]
    public void AssembleTag_WithoutIntegrity_Unchanged() {
        var spec = new ImageBuildSpec("11.0.0", true, true, true);

        Assert.Equal("redroid/redroid:11.0.0_gapps_ndk_magisk", spec.ResolvedTag);
    }

    [Fact]
    public void AssembleTag_WithIntegrity_AppendsTrailingToken() {
        var spec = new ImageBuildSpec("11.0.0", true, true, true, Integrity: true);

        Assert.Equal("redroid/redroid:11.0.0_gapps_ndk_magisk_integrity", spec.ResolvedTag);
    }

    [Theory]
    [InlineData("redroid/redroid:11.0.0_gapps_ndk_magisk_integrity", true)]
    [InlineData("redroid/redroid:11.0.0_gapps_ndk_magisk_integrity@sha256:abc", false)]
    [InlineData("redroid/redroid:11.0.0_gapps_ndk_magisk", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksSeeded_ReadsTheTagToken(string? image, bool expected) =>
        Assert.Equal(expected, ImageBuildSpec.LooksSeeded(image));

    [Fact]
    public void ResolvedTag_ExplicitTagWins() {
        var spec = new ImageBuildSpec("11.0.0", false, false, false, Tag: "custom:tag", Integrity: true);

        Assert.Equal("custom:tag", spec.ResolvedTag);
    }
}
