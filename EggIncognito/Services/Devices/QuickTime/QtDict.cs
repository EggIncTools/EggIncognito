using System.Buffers.Binary;
using System.Text;

namespace EggIncognito.Services.Devices.QuickTime;

internal static class QtDict {
    private const byte NumberInt32 = 0x03;
    private const byte NumberFloat64 = 0x06;

    private static byte[] Block(uint magic, ReadOnlySpan<byte> payload) {
        byte[] b = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)b.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), magic);
        payload.CopyTo(b.AsSpan(8));
        return b;
    }

    private static byte[] StringKey(string s) => Block(QtMagic.Strk, Encoding.UTF8.GetBytes(s));
    private static byte[] StringValue(string s) => Block(QtMagic.Strv, Encoding.UTF8.GetBytes(s));
    private static byte[] DataValue(ReadOnlySpan<byte> d) => Block(QtMagic.Datv, d);

    private static byte[] BoolValue(bool v) => Block(QtMagic.Bulv, [v ? (byte)1 : (byte)0]);

    private static byte[] NumberF64(double v) {
        Span<byte> p = stackalloc byte[9];
        p[0] = NumberFloat64;
        BinaryPrimitives.WriteDoubleLittleEndian(p[1..], v);
        return Block(QtMagic.Nmbv, p);
    }

    private static byte[] NumberI32(uint v) {
        Span<byte> p = stackalloc byte[5];
        p[0] = NumberInt32;
        BinaryPrimitives.WriteUInt32LittleEndian(p[1..], v);
        return Block(QtMagic.Nmbv, p);
    }

    private static byte[] Pair(byte[] key, byte[] value) {
        byte[] p = new byte[key.Length + value.Length];
        key.CopyTo(p, 0);
        value.CopyTo(p, key.Length);
        return Block(QtMagic.Keyv, p);
    }

    private static byte[] Concat(params byte[][] parts) {
        byte[] all = new byte[parts.Sum(p => p.Length)];
        int at = 0;
        foreach (byte[] p in parts) {
            p.CopyTo(all, at);
            at += p.Length;
        }
        return all;
    }

    private static byte[] Dict(params byte[][] pairs) => Block(QtMagic.Dict, Concat(pairs));

    public static byte[] ErrorZero() =>
        Dict(Pair(StringKey("Error"), NumberI32(0)));

    public static byte[] Hpd1(int width, int height) =>
        Dict(
            Pair(StringKey("Valeria"), BoolValue(true)),
            Pair(StringKey("HEVCDecoderSupports444"), BoolValue(true)),
            Pair(StringKey("DisplaySize"), Dict(
                Pair(StringKey("Width"), NumberF64(width)),
                Pair(StringKey("Height"), NumberF64(height)))));

    public static byte[] Hpa1() =>
        Dict(
            Pair(StringKey("BufferAheadInterval"), NumberF64(0.07300000000000001)),
            Pair(StringKey("deviceUID"), StringValue("Valeria")),
            Pair(StringKey("ScreenLatency"), NumberF64(0.04)),
            Pair(StringKey("formats"), DataValue(QtAudio.Asbd())),
            Pair(StringKey("EDIDAC3Support"), NumberI32(0)),
            Pair(StringKey("deviceName"), StringValue("Valeria")));
}

internal static class QtAudio {
    private const uint FormatLpcm = 0x6C70636D;
    public const double SampleRate = 48000.0;

    public static byte[] Asbd() {
        byte[] b = new byte[56];
        BinaryPrimitives.WriteDoubleLittleEndian(b, SampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), FormatLpcm);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), 12);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(24), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(28), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(32), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(36), 0);
        BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan(40), SampleRate);
        BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan(48), SampleRate);
        return b;
    }
}
