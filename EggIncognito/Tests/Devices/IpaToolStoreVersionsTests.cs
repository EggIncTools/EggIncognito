using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class IpaToolStoreVersionsTests {
    [Fact]
    public void Parse_AppsEnvelope_ReadsVersionAndDate() {
        var r = IpaToolStoreVersions.Parse(
            """{"count":1,"apps":[{"bundleID":"com.auxbrain.egginc","version":"1.37.2","currentVersionReleaseDate":"2026-09-10T12:00:00Z"}]}""");

        Assert.True(r.Ok);
        var v = Assert.Single(r.Versions);
        Assert.Equal("1.37.2", v.AppVersion);
        Assert.Equal(DateTimeOffset.Parse("2026-09-10T12:00:00Z"), v.ReleaseDate);
    }

    [Fact]
    public void Parse_BareArray_IsAccepted() {
        var r = IpaToolStoreVersions.Parse("""[{"version":"1.36"}]""");

        Assert.True(r.Ok);
        Assert.Equal("1.36", Assert.Single(r.Versions).AppVersion);
    }

    [Fact]
    public void Parse_ResultsEnvelope_IsAccepted() {
        var r = IpaToolStoreVersions.Parse("""{"results":[{"version":"1.35.8"}]}""");

        Assert.True(r.Ok);
        Assert.Equal("1.35.8", Assert.Single(r.Versions).AppVersion);
    }

    [Fact]
    public void Parse_NdjsonWithLogNoise_FindsTheAppLine() {
        var r = IpaToolStoreVersions.Parse(
            "level=info msg=\"searching\"\n{\"apps\":[{\"version\":\"1.37\"}]}\nlevel=info msg=\"done\"");

        Assert.True(r.Ok);
        Assert.Equal("1.37", Assert.Single(r.Versions).AppVersion);
    }

    [Fact]
    public void Parse_MissingVersionField_IsNotOk() {
        var r = IpaToolStoreVersions.Parse("""{"apps":[{"bundleID":"com.auxbrain.egginc"}]}""");

        Assert.False(r.Ok);
        Assert.Empty(r.Versions);
    }

    [Fact]
    public void Parse_EmptyOrGarbage_IsNotOk() {
        Assert.False(IpaToolStoreVersions.Parse("").Ok);
        Assert.False(IpaToolStoreVersions.Parse("   ").Ok);
        Assert.False(IpaToolStoreVersions.Parse("not json at all").Ok);
    }

    [Fact]
    public void Parse_UnparseableDate_KeepsVersionWithNullDate() {
        var r = IpaToolStoreVersions.Parse("""{"apps":[{"version":"1.20","releaseDate":"whenever"}]}""");

        Assert.True(r.Ok);
        Assert.Null(Assert.Single(r.Versions).ReleaseDate);
    }

    [Fact]
    public void Enabled_IsFalse_WhenBinaryPathMissing() {
        var config = new IpaToolsConfig { IpaToolPath = "/var/run/definitely-not-here-egi" };

        Assert.False(config.Enabled);
    }
}
