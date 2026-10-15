using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class ProtoVersionTranslatorTests {
    private sealed record Row(
        string Platform, string AppVersion, string Build, string? Client = null,
        long? Release = null, string? Sha = null);

    private static VersionKey Key(Row r) => new(r.Platform, r.AppVersion, r.Build, r.Client, null, null, r.Release, r.Sha);

    [Fact]
    public void CanonicalReleaseIdWinsOverEveryWeakerLink() {
        var src = new Row("ios", "1.37.0", "1.37.0.1", "75", Release: 5, Sha: "abc");
        var rows = new[] {
            src,
            new Row("android", "9.9.9", "111400", "99", Release: 5, Sha: "zzz"),
            new Row("android", "1.37.0", "111358", "75", Sha: "abc"),
        };
        var link = ProtoVersionTranslator.Translate(src, "android", rows, Key);
        Assert.Equal(VersionLinkKind.Canonical, link.Kind);
        Assert.Equal("111400", link.Row!.Build);
    }

    [Fact]
    public void ProtoShaWinsOverAppVersion() {
        var src = new Row("ios", "1.37.0", "1.37.0.1", "75", Sha: "abc");
        var rows = new[] {
            src,
            new Row("android", "9.9.9", "111400", "99", Sha: "abc"),
            new Row("android", "1.37.0", "111358", "75"),
        };
        var link = ProtoVersionTranslator.Translate(src, "android", rows, Key);
        Assert.Equal(VersionLinkKind.ProtoSha, link.Kind);
        Assert.Equal("111400", link.Row!.Build);
    }

    [Fact]
    public void AppVersionWinsOverClientVersion() {
        var src = new Row("ios", "1.37.0", "1.37.0.1", "75");
        var rows = new[] {
            src,
            new Row("android", "1.37.0", "111358", "70"),
            new Row("android", "1.30.0", "111000", "75"),
        };
        var link = ProtoVersionTranslator.Translate(src, "android", rows, Key);
        Assert.Equal(VersionLinkKind.AppVersion, link.Kind);
        Assert.Equal("111358", link.Row!.Build);
    }

    [Fact]
    public void ClientVersionIsTheLastResort() {
        var src = new Row("ios", "1.37.0", "1.37.0.1", "75");
        var rows = new[] { src, new Row("android", "9.9.9", "111400", "75") };
        var link = ProtoVersionTranslator.Translate(src, "android", rows, Key);
        Assert.Equal(VersionLinkKind.ClientVersion, link.Kind);
        Assert.Equal("111400", link.Row!.Build);
    }

    [Fact]
    public void TheNewestCandidateWinsInsideATier() {
        var src = new Row("ios", "1.37.0", "1.37.0.1", "75");
        var rows = new[] {
            src,
            new Row("android", "1.37.0", "111341", "75"),
            new Row("android", "1.37.0", "111358", "75"),
        };
        Assert.Equal("111358", ProtoVersionTranslator.Translate(src, "android", rows, Key).Row!.Build);
    }

    [Fact]
    public void NothingComparableYieldsNone() {
        var src = new Row("ios", "1.37.0", "1.37.0.1", "75", Sha: "abc");
        var rows = new[] { src, new Row("android", "1.20.0", "110000", "60", Sha: "def") };
        var link = ProtoVersionTranslator.Translate(src, "android", rows, Key);
        Assert.Equal(VersionLinkKind.None, link.Kind);
        Assert.Null(link.Row);
    }

    [Fact]
    public void TheSourceRowIsNeverItsOwnMatch() {
        var src = new Row("ios", "1.37.0", "1.37.0.1", "75", Release: 5, Sha: "abc");
        var link = ProtoVersionTranslator.Translate(src, "ios", new[] { src }, Key);
        Assert.Equal(VersionLinkKind.None, link.Kind);
    }

    [Fact]
    public void EmptyKeyFieldsNeverMatchEachOther() {
        var src = new Row("ios", "", "1.37.0.1");
        var rows = new[] { src, new Row("android", "", "111358") };
        Assert.Equal(VersionLinkKind.None, ProtoVersionTranslator.Translate(src, "android", rows, Key).Kind);
    }

    [Fact]
    public void TranslateAllCoversEveryOtherPlatformInFixedOrder() {
        var src = new Row("ios", "1.37.0", "1.37.0.1", "75");
        var rows = new[] {
            src,
            new Row("web", "1.37.0", "w37", "75"),
            new Row("android", "1.37.0", "111358", "75"),
        };
        var links = ProtoVersionTranslator.TranslateAll(src, rows, Key);
        Assert.Equal(new[] { "111358", "w37" }, links.Select(l => l.Row!.Build).ToArray());
    }
}
