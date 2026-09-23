using System.IO.Compression;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class LibegincClientVersion {
    private const long MaxSoBytes = 300_000_000L;
    private static readonly string[] SymbolNeedles = ["GameController20currentClientVersion"];

    public static int? Read(byte[] apkOrSoBytes, int? prevClientVersion = null) {
        _ = prevClientVersion;
        try {
            byte[]? bin = IsZip(apkOrSoBytes) ? ReadSoFromZip(apkOrSoBytes) : apkOrSoBytes;
            return bin is null ? null : ReadFromBinary(bin);
        } catch {
            return null;
        }
    }

    public static int? ReadFromBinary(byte[] bin) => ReadFromBinary(bin, null);

    public static int? ReadFromBinary(byte[] bin, IReadOnlyList<MachoSymbols.Symbol>? symbols) {
        if (bin is null || bin.Length < 8) return null;
        var img = BinaryImage.Load(bin);
        if (img is null) return null;
        bool arm32 = IsElf32(bin);

        if ((img.TryFindFunc(SymbolNeedles, out var fn)
             || (symbols is not null && MachoSymbols.TryFindFunc(symbols, SymbolNeedles, out fn)))
            && DecodeGetter(img, bin, fn, arm32) is { } viaSymbol) {
            return viaSymbol;
        }

        if (arm32) return null;
        return ScanBasicRequestInfoCv(img) ?? ScanPerRequestCv(img);
    }

    private static int? DecodeGetter(IBinaryImage img, byte[] bin, MachoSymbols.FuncRange fn, bool arm32) {
        bool thumb = arm32 && (fn.Start & 1) != 0;
        ulong startVa = arm32 ? fn.Start & ~1UL : fn.Start;
        if (!img.TryVaToFileOffset(startVa, out int fo, out _)) return null;
        if (arm32) {
            int win = (int)Math.Min(64L, (long)bin.Length - fo);
            return fo < 0 || win < 2 ? null : DecodeArm32ConstReturn(bin, fo, win, thumb);
        }

        ulong span = fn.End > fn.Start ? fn.End - fn.Start : 16UL;
        int len = (int)Math.Min(span, 64UL);
        return len < 4 || fo < 0 || fo + len > bin.Length ? null : DecodeConstReturn(bin, fo, len);
    }

    private const int CvFieldOffset = 0x38;
    private const int HasBitsFieldOffset = 0x10;
    private const int HasBitsMask = 0x12;
    private const int MaxClientVersion = 500;

    private static int? ScanBasicRequestInfoCv(IBinaryImage img) {
        if (!img.TryFindText(out int fo, out int size, out _)) return null;
        byte[] b = img.Bytes;
        long end = Math.Min((long)fo + size, b.Length);
        if (fo < 0 || end - fo < 20) return null;

        for (long p = fo; p + 20 <= end; p += 4) {
            uint mov = Word(b, p);
            if ((mov & 0xFFE00000) != 0x52800000) continue;
            int cvReg = (int)(mov & 0x1F);
            int cv = (int)((mov >> 5) & 0xFFFF);
            if (cv is < 1 or > MaxClientVersion) continue;

            uint strCv = Word(b, p + 4);
            if ((strCv & 0xFFC00000) != 0xB9000000) continue;
            if (((strCv >> 10) & 0xFFF) != CvFieldOffset / 4) continue;
            if ((int)(strCv & 0x1F) != cvReg) continue;
            int baseReg = (int)((strCv >> 5) & 0x1F);

            uint movMask = Word(b, p + 8);
            if ((movMask & 0xFFE00000) != 0x52800000) continue;
            if (((movMask >> 5) & 0xFFFF) != HasBitsMask) continue;

            uint strHas = Word(b, p + 16);
            if ((strHas & 0xFFC00000) != 0xB9000000) continue;
            if (((strHas >> 10) & 0xFFF) != HasBitsFieldOffset / 4) continue;
            if ((int)((strHas >> 5) & 0x1F) != baseReg) continue;

            return cv;
        }

        return null;
    }

    private const int MinPerRequestCallSites = 2;

    private static int? ScanPerRequestCv(IBinaryImage img) {
        if (!img.TryFindText(out int fo, out int size, out ulong textVa)) return null;
        byte[] b = img.Bytes;
        long end = Math.Min((long)fo + size, b.Length);
        if (fo < 0 || end - fo < 32) return null;

        var hits = new Dictionary<long, (int Value, int Sites)>();
        for (long p = fo; p + 24 <= end; p += 4) {
            uint bl = Word(b, p);
            if ((bl & 0xFC000000) != 0x94000000) continue;
            if (!IsProtoFieldStoreSequence(b, p, end)) continue;

            long target = p + 4L * SignExtend26(bl & 0x03FFFFFF);
            if (target < fo || target + 8 > end) continue;
            if (Word(b, target + 4) != 0xD65F03C0) continue;
            if (DecodeConstReturn(b, (int)target, 8) is not { } cv) continue;
            if (cv is < 1 or > MaxClientVersion) continue;

            hits[target] = hits.TryGetValue(target, out var seen)
                ? (cv, seen.Sites + 1)
                : (cv, 1);
        }

        _ = textVa;
        var best = hits.Values.Where(h => h.Sites >= MinPerRequestCallSites).ToList();
        return best.Count == 1 ? best[0].Value : null;
    }

    private static bool IsProtoFieldStoreSequence(byte[] b, long blAt, long end) {
        bool storesW0 = false, hasBitOr = false, followUpCall = false;
        for (long q = blAt + 4; q <= blAt + 16 && q + 4 <= end; q += 4) {
            uint w = Word(b, q);
            if ((w & 0xFFC00000) == 0xB9000000 && (w & 0x1F) == 0) storesW0 = true;
            if ((w & 0xFF800000) == 0x32000000) hasBitOr = true;
        }

        for (long q = blAt + 16; q <= blAt + 40 && q + 4 <= end; q += 4) {
            if ((Word(b, q) & 0xFC000000) == 0x94000000) followUpCall = true;
        }

        return storesW0 && hasBitOr && followUpCall;
    }

    private static long SignExtend26(uint imm26) =>
        imm26 >= 1u << 25 ? (long)imm26 - (1L << 26) : imm26;

    private static uint Word(byte[] b, long p) =>
        (uint)(b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24));

    private static bool IsElf32(byte[] b) =>
        b is { Length: >= 8 } && b[0] == 0x7F && b[1] == (byte)'E' && b[2] == (byte)'L' && b[3] == (byte)'F' && b[4] == 1;

    private static int? DecodeArm32ConstReturn(byte[] bin, int fo, int len, bool thumb) =>
        thumb ? DecodeThumbConstReturn(bin, fo, len) : DecodeArmConstReturn(bin, fo, len);

    private static int? DecodeThumbConstReturn(byte[] bin, int fo, int len) {
        long? r0 = null;
        for (int p = fo; p + 2 <= fo + len;) {
            ushort hw = (ushort)(bin[p] | (bin[p + 1] << 8));
            if (hw == 0x4770) break;
            if ((hw & 0xF800) == 0x2000) {
                if (((hw >> 8) & 7) == 0) r0 = hw & 0xFF;
                p += 2;
                continue;
            }

            if ((hw & 0xFBF0) == 0xF240 && p + 4 <= fo + len) {
                ushort hw2 = (ushort)(bin[p + 2] | (bin[p + 3] << 8));
                int i = (hw >> 10) & 1;
                int imm4 = hw & 0xF;
                int imm3 = (hw2 >> 12) & 7;
                int rd = (hw2 >> 8) & 0xF;
                int imm8 = hw2 & 0xFF;
                if (rd == 0) r0 = (imm4 << 12) | (i << 11) | (imm3 << 8) | imm8;
                p += 4;
                continue;
            }

            p += 2;
        }

        return r0 is null or < 0 or > int.MaxValue ? null : (int)r0.Value;
    }

    private static int? DecodeArmConstReturn(byte[] bin, int fo, int len) {
        long? r0 = null;
        for (int p = fo; p + 4 <= fo + len; p += 4) {
            uint ins = (uint)(bin[p] | (bin[p + 1] << 8) | (bin[p + 2] << 16) | (bin[p + 3] << 24));
            if ((ins & 0x0FFFFFFF) == 0x012FFF1E) break;
            if ((ins & 0x0FEF0000) == 0x03A00000) {
                int rd = (int)((ins >> 12) & 0xF);
                int rot = (int)((ins >> 8) & 0xF) * 2;
                uint imm8 = ins & 0xFF;
                uint val = rot == 0 ? imm8 : (imm8 >> rot) | (imm8 << (32 - rot));
                if (rd == 0) r0 = val;
            } else if ((ins & 0x0FF00000) == 0x03000000) {
                int rd = (int)((ins >> 12) & 0xF);
                if (rd == 0) r0 = (int)(((ins >> 16) & 0xF) << 12 | (ins & 0xFFF));
            }
        }

        return r0 is null or < 0 or > int.MaxValue ? null : (int)r0.Value;
    }

    private static int? DecodeConstReturn(byte[] bin, int fo, int len) {
        long? w0 = null;
        for (int p = fo; p + 4 <= fo + len; p += 4) {
            uint ins = (uint)(bin[p] | (bin[p + 1] << 8) | (bin[p + 2] << 16) | (bin[p + 3] << 24));
            if (ins == 0xD65F03C0) break;
            int rd = (int)(ins & 0x1F);
            int hw = (int)((ins >> 21) & 3);
            int imm16 = (int)((ins >> 5) & 0xFFFF);
            if ((ins & 0x7F800000) == 0x52800000) {
                if (rd == 0) w0 = (long)imm16 << (hw * 16);
            } else if ((ins & 0x7F800000) == 0x72800000) {
                if (rd == 0 && w0 is not null) {
                    long mask = ~((long)0xFFFF << (hw * 16));
                    w0 = (w0.Value & mask) | ((long)imm16 << (hw * 16));
                }
            } else if ((ins & 0x7F800000) == 0x32000000 && rd == 0 && ((ins >> 5) & 0x1F) == 31) {
                ulong? bm = DecodeBitMask((int)((ins >> 22) & 1), (int)((ins >> 16) & 0x3F),
                    (int)((ins >> 10) & 0x3F), ((ins >> 31) & 1) != 0 ? 64 : 32);
                if (bm is not null) w0 = (long)bm.Value;
            }
        }

        return w0 is null or < 0 or > int.MaxValue ? null : (int)w0.Value;
    }

    private static ulong? DecodeBitMask(int n, int immr, int imms, int width) {
        int combined = (n << 6) | (imms ^ 0x3F);
        if (combined == 0) return null;
        int length = 31 - System.Numerics.BitOperations.LeadingZeroCount((uint)combined);
        int esize = 1 << length;
        if (esize is < 2 or > 64) return null;
        int levels = esize - 1;
        int s = imms & levels;
        int r = immr & levels;
        ulong welem = s + 1 >= 64 ? ulong.MaxValue : (1UL << (s + 1)) - 1;
        ulong elemMask = esize >= 64 ? ulong.MaxValue : (1UL << esize) - 1;
        ulong val = r == 0 ? welem : ((welem >> r) | (welem << (esize - r))) & elemMask;
        ulong outp = 0;
        for (int i = 0; i < width; i += esize) outp |= val << i;
        return width >= 64 ? outp : outp & ((1UL << width) - 1);
    }

    private static bool IsZip(byte[] b) =>
        b is { Length: > 4 } && b[0] == 0x50 && b[1] == 0x4B && b[2] == 0x03 && b[3] == 0x04;

    private static byte[]? ReadSoFromZip(byte[] zipBytes) {
        try {
            using var zip = new ZipArchive(new MemoryStream(zipBytes, false), ZipArchiveMode.Read);
            var entry = zip.GetEntry("lib/arm64-v8a/libegginc.so")
                        ?? zip.Entries.FirstOrDefault(e =>
                            e.FullName.Contains("arm64", StringComparison.OrdinalIgnoreCase)
                            && e.FullName.EndsWith("/libegginc.so", StringComparison.OrdinalIgnoreCase))
                        ?? zip.Entries.FirstOrDefault(e =>
                            e.FullName.EndsWith("/libegginc.so", StringComparison.OrdinalIgnoreCase));
            if (entry is null || entry.Length is <= 0 or > MaxSoBytes) return null;
            using var es = entry.Open();
            using var buf = new MemoryStream();
            es.CopyTo(buf);
            return buf.ToArray();
        } catch {
            return null;
        }
    }
}
