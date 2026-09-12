using System.Buffers.Binary;

namespace EggIncognito.Services.Devices.QuickTime;

internal static class QtSampleBuffer {
    private const int OptsBlockSize = 32;
    private static ReadOnlySpan<byte> StartCode => [0x00, 0x00, 0x00, 0x01];

    public static bool TryReadSdat(ReadOnlySpan<byte> sbuf, out ReadOnlySpan<byte> sdat,
        out ReadOnlySpan<byte> avcC) {
        sdat = default;
        avcC = default;
        if (sbuf.Length < 8) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(sbuf[4..]) != QtMagic.Sbuf) return false;

        int at = 8;
        while (at + 8 <= sbuf.Length) {
            uint blockLen = BinaryPrimitives.ReadUInt32LittleEndian(sbuf[at..]);
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(sbuf[(at + 4)..]);
            if (magic == QtMagic.Opts) {
                at += OptsBlockSize;
                continue;
            }
            if (blockLen < 8 || at + blockLen > sbuf.Length) return false;
            var payload = sbuf.Slice(at + 8, (int)blockLen - 8);
            if (magic == QtMagic.Sdat) sdat = payload;
            else if (magic == QtMagic.Fdsc) TryReadAvcC(payload, out avcC);
            at += (int)blockLen;
        }
        return !sdat.IsEmpty;
    }

    private static bool TryReadAvcC(ReadOnlySpan<byte> fdsc, out ReadOnlySpan<byte> avcC) {
        avcC = default;
        int at = 0;
        while (at + 8 <= fdsc.Length) {
            uint blockLen = BinaryPrimitives.ReadUInt32LittleEndian(fdsc[at..]);
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(fdsc[(at + 4)..]);
            if (blockLen < 8 || at + blockLen > fdsc.Length) return false;
            if (magic == QtMagic.Extn)
                return TryFindDatv(fdsc.Slice(at + 8, (int)blockLen - 8), out avcC);
            at += (int)blockLen;
        }
        return false;
    }

    private static bool TryFindDatv(ReadOnlySpan<byte> body, out ReadOnlySpan<byte> found) {
        found = default;
        int at = 0;
        while (at + 8 <= body.Length) {
            uint blockLen = BinaryPrimitives.ReadUInt32LittleEndian(body[at..]);
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(body[(at + 4)..]);
            if (blockLen < 8 || at + blockLen > body.Length) return false;
            var payload = body.Slice(at + 8, (int)blockLen - 8);
            if (magic == QtMagic.Datv) {
                found = payload;
                return true;
            }
            if (magic is QtMagic.Dict or QtMagic.Keyv && TryFindDatv(payload, out found)) return true;
            at += (int)blockLen;
        }
        return false;
    }

    public static bool TryReadParameterSets(ReadOnlySpan<byte> avcC, out byte[] sps, out byte[] pps) {
        sps = [];
        pps = [];
        if (avcC.Length < 7 || avcC[0] != 0x01) return false;
        int spsLen = BinaryPrimitives.ReadUInt16BigEndian(avcC[6..]);
        if (8 + spsLen > avcC.Length) return false;
        sps = avcC.Slice(8, spsLen).ToArray();
        int at = 8 + spsLen;
        if (at + 3 > avcC.Length) return false;
        int ppsLen = BinaryPrimitives.ReadUInt16BigEndian(avcC[(at + 1)..]);
        if (at + 3 + ppsLen > avcC.Length) return false;
        pps = avcC.Slice(at + 3, ppsLen).ToArray();
        return true;
    }

    public static int LengthPrefixSize(ReadOnlySpan<byte> avcC) =>
        avcC.Length > 4 ? (avcC[4] & 0x03) + 1 : 4;

    public static async Task WriteAnnexBAsync(Stream output, byte[] nalu, CancellationToken ct) {
        await output.WriteAsync(StartCode.ToArray(), ct);
        await output.WriteAsync(nalu, ct);
    }

    public static List<byte[]> SplitNalus(ReadOnlySpan<byte> sdat, int prefixSize) {
        var nalus = new List<byte[]>();
        int at = 0;
        while (at + prefixSize <= sdat.Length) {
            long len = prefixSize switch {
                4 => BinaryPrimitives.ReadUInt32BigEndian(sdat[at..]),
                3 => (sdat[at] << 16) | (sdat[at + 1] << 8) | sdat[at + 2],
                2 => BinaryPrimitives.ReadUInt16BigEndian(sdat[at..]),
                _ => sdat[at]
            };
            at += prefixSize;
            if (len <= 0 || at + len > sdat.Length) break;
            nalus.Add(sdat.Slice(at, (int)len).ToArray());
            at += (int)len;
        }
        return nalus;
    }
}
