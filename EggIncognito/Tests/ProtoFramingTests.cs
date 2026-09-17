using System.IO.Compression;
using EggIncognito.Core.Services;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Tests;

public class ProtoFramingTests {
    private static byte[] GzipZeros(long count) {
        byte[] chunk = new byte[64 * 1024];
        using var o = new MemoryStream();
        using (var gz = new GZipStream(o, CompressionLevel.Optimal, true)) {
            for (long written = 0; written < count; written += chunk.Length)
                gz.Write(chunk, 0, (int)Math.Min(chunk.Length, count - written));
        }

        return o.ToArray();
    }

    private static byte[] Gzip(byte[] data) {
        using var o = new MemoryStream();
        using (var gz = new GZipStream(o, CompressionLevel.Fastest, true)) gz.Write(data);
        return o.ToArray();
    }

    [Fact]
    public void Decompress_GzipBombOverCap_ThrowsInvalidData() {
        byte[] bomb = GzipZeros(96L * 1024 * 1024);
        Assert.True(bomb.Length < ProtoFraming.MaxInflatedBytes);
        Assert.Throws<InvalidDataException>(() => ProtoFraming.Decompress(bomb));
    }

    [Fact]
    public void TryUnwrap_CompressedBomb_ReturnsNull() {
        byte[] bomb = GzipZeros(96L * 1024 * 1024);
        var outer = new AuthenticatedMessage { Compressed = true, Message = ByteString.CopyFrom(bomb) };
        Assert.Null(ProtoFraming.TryUnwrap(outer.ToByteArray()));
    }

    [Fact]
    public void Decompress_SmallGzip_RoundTrips() {
        byte[] payload = [0x08, 0x96, 0x01, 0xff, 0x00, 0x42];
        Assert.Equal(payload, ProtoFraming.Decompress(Gzip(payload)));
    }
}
