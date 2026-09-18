using System.IO.Compression;
using System.Text;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class ArchiveProtoExtractorTests {
    [Fact]
    public void Extract_Apk_LibEgginc_Carves() {
        if (!TryFixture(out byte[] fx)) return;
        byte[] apk = ZipWith("lib/arm64-v8a/libegginc.so", fx);
        var r = ArchiveProtoExtractor.Extract(apk);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Contains("message EggIncFirstContactRequest {", r.Proto);
        Assert.NotEmpty(r.Messages);
    }

    [Fact]
    public void Extract_Ipa_CompressedAppExecutable_Carves() {
        if (!TryFixture(out byte[] fx)) return;
        byte[] ipa = ZipWith("Payload/EggInc.app/egginc", fx);
        var r = ArchiveProtoExtractor.Extract(ipa);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Contains("message EggIncFirstContactRequest {", r.Proto);
    }

    [Fact]
    public void Extract_Ipa_IgnoresAppResources_FindsExecutable() {
        if (!TryFixture(out byte[] fx)) return;
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true)) {
            Write(zip, "Payload/EggInc.app/Info.plist", [1, 2, 3]);
            Write(zip, "Payload/EggInc.app/egginc", fx);
            Write(zip, "Payload/EggInc.app/Assets.car", [4, 5, 6]);
        }

        var r = ArchiveProtoExtractor.Extract(ms.ToArray());
        Assert.True(r.Ok, r.Diagnostics);
    }

    [Fact]
    public void Extract_DescriptorInStoredEntry_FallsBackToRawScan() {
        if (!TryFixture(out byte[] fx)) return;
        byte[] apk = ZipWith("assets/blob.bin", fx, CompressionLevel.NoCompression);
        var r = ArchiveProtoExtractor.Extract(apk);
        Assert.True(r.Ok, r.Diagnostics);
    }

    [Fact]
    public void Extract_Ipa_ReadsVersionFromInfoPlist() {
        if (!TryFixture(out byte[] fx)) return;
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true)) {
            Write(zip, "Payload/EggInc.app/egginc", fx);
            Write(zip, "Payload/EggInc.app/Info.plist", Encoding.UTF8.GetBytes(
                "<plist><dict><key>CFBundleShortVersionString</key><string>1.35.6</string>"
                + "<key>CFBundleVersion</key><string>1.35.6.3</string></dict></plist>"));
        }

        var r = ArchiveProtoExtractor.Extract(ms.ToArray());
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal("1.35.6", r.AppVersion);
        Assert.Equal("1.35.6.3", r.Build);
    }

    [Fact]
    public void Extract_Ipa_WithoutBundleVersion_LeavesBuildNull() {
        if (!TryFixture(out byte[] fx)) return;
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true)) {
            Write(zip, "Payload/EggInc.app/egginc", fx);
            Write(zip, "Payload/EggInc.app/Info.plist", Encoding.UTF8.GetBytes(
                "<plist><dict><key>CFBundleShortVersionString</key><string>1.35.6</string></dict></plist>"));
        }

        var r = ArchiveProtoExtractor.Extract(ms.ToArray());
        Assert.Equal("1.35.6", r.AppVersion);
        Assert.Null(r.Build);
    }

    [Fact]
    public void Extract_Empty_FailsCleanly() => Assert.False(ArchiveProtoExtractor.Extract([]).Ok);

    [Fact]
    public void Extract_NotAZip_RawScanCarves() {
        if (!TryFixture(out byte[] fx)) return;
        var r = ArchiveProtoExtractor.Extract(fx);
        Assert.True(r.Ok, r.Diagnostics);
    }

    private static byte[] ZipWith(string name, byte[] content, CompressionLevel level = CompressionLevel.Optimal) {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            Write(zip, name, content, level);
        return ms.ToArray();
    }

    private static void Write(ZipArchive zip, string name, byte[] content,
        CompressionLevel level = CompressionLevel.Optimal) {
        using var es = zip.CreateEntry(name, level).Open();
        es.Write(content);
    }

    private static bool TryFixture(out byte[] bytes) =>
        TestFixtureFiles.TryRead("egginc-1.35.8-descriptors.bin", out bytes);
}
