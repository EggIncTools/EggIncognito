using System.IO.Compression;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Core.Services;

public static class ProtoFraming {
    public const int MaxInflatedBytes = 64 * 1024 * 1024;

    public static byte[] FromBase64Loose(string s) {
        s = s.Trim().Replace(' ', '+');
        int pad = s.Length % 4;
        if (pad != 0) s = s.PadRight(s.Length + (4 - pad), '=');
        return Convert.FromBase64String(s);
    }

    public static byte[] Decompress(byte[] compressed) {
        if (compressed.Length >= 2 && compressed[0] == 0x1f && compressed[1] == 0x8b)
            return Inflate(compressed, input => new GZipStream(input, CompressionMode.Decompress));

        return TryInflate(compressed, input => new ZLibStream(input, CompressionMode.Decompress))
               ?? TryInflate(compressed, input => new DeflateStream(input, CompressionMode.Decompress))
               ?? compressed;
    }

    private static byte[] Inflate(byte[] compressed, Func<Stream, Stream> wrap) {
        using var input = new MemoryStream(compressed);
        using var decompressor = wrap(input);
        using var output = new MemoryStream();
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = decompressor.Read(buffer)) > 0) {
            total += read;
            if (total > MaxInflatedBytes) throw new InvalidDataException("decompressed payload exceeds 64 MiB");
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static byte[]? TryInflate(byte[] compressed, Func<Stream, Stream> wrap) {
        try {
            return Inflate(compressed, wrap);
        } catch (InvalidDataException) {
            return null;
        }
    }

    public static byte[] Unwrap(byte[] bytes) {
        var outer = AuthenticatedMessage.Parser.ParseFrom(bytes);
        return outer.Compressed ? Decompress(outer.Message.ToByteArray()) : outer.Message.ToByteArray();
    }

    public static byte[]? TryUnwrap(byte[] bytes) {
        try {
            var outer = AuthenticatedMessage.Parser.ParseFrom(bytes);
            return outer.Message.Length == 0 ? null :
                outer.Compressed ? Decompress(outer.Message.ToByteArray()) : outer.Message.ToByteArray();
        } catch (Exception ex) when (ex is InvalidProtocolBufferException or InvalidDataException) {
            return null;
        }
    }
}
