using System.IO.Compression;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class ApkVersionCodeTests {
    private const string ExpectedVersionCode = "111344";

    [Fact]
    public void Read_NullBytes_ReturnsNull() => Assert.Null(ApkVersionCode.Read(null!));

    [Fact]
    public void Read_EmptyBytes_ReturnsNull() => Assert.Null(ApkVersionCode.Read([]));

    [Fact]
    public void Read_GarbageBytes_ReturnsNull() =>
        Assert.Null(ApkVersionCode.Read([0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11, 0x22, 0x33]));

    [Fact]
    public void Read_ZipWithoutManifest_ReturnsNull() =>
        Assert.Null(ApkVersionCode.Read(ZipWithEntry("classes.dex", [1, 2, 3])));

    [Fact]
    public void Read_RealArmSplitManifestZip_ReturnsVersionCode() {
        if (!TryFixture(out byte[] fx)) return;
        Assert.Equal(ExpectedVersionCode, ApkVersionCode.Read(ZipWithEntry("AndroidManifest.xml", fx)));
    }

    private static bool TryFixture(out byte[] bytes) =>
        TestFixtureFiles.TryRead("arm_split_AndroidManifest.bin", out bytes);

    private static byte[] ZipWithEntry(string name, byte[] content) {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        using (var es = zip.CreateEntry(name).Open())
            es.Write(content);
        return ms.ToArray();
    }
}
