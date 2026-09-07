using System.Buffers.Binary;

namespace EggIncognito.Core.Services.Devices;

public readonly record struct BridgeFrame(byte Kind, byte[] Payload) {
    public int ExitCode => Payload.Length >= 4 ? BinaryPrimitives.ReadInt32BigEndian(Payload) : -1;
}

public static class BridgeStreamFrames {
    public const byte Stdout = 1;
    public const byte Stderr = 2;
    public const byte Exit = 3;
    private const int HeaderLength = 5;
    public const int MaxPayload = 16 * 1024 * 1024;

    public static async Task WriteAsync(Stream stream, byte kind, ReadOnlyMemory<byte> payload, CancellationToken ct) {
        byte[] header = new byte[HeaderLength];
        header[0] = kind;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1), payload.Length);
        await stream.WriteAsync(header, ct);
        if (payload.Length > 0) await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    public static Task WriteExitAsync(Stream stream, int code, CancellationToken ct) {
        byte[] payload = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(payload, code);
        return WriteAsync(stream, Exit, payload, ct);
    }

    public static async Task<BridgeFrame?> ReadAsync(Stream stream, CancellationToken ct) {
        byte[] header = new byte[HeaderLength];
        if (!await FillAsync(stream, header, ct)) return null;

        int length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1));
        if (length is < 0 or > MaxPayload) return null;

        byte[] payload = length == 0 ? [] : new byte[length];
        if (length > 0 && !await FillAsync(stream, payload, ct)) return null;
        return new BridgeFrame(header[0], payload);
    }

    private static async Task<bool> FillAsync(Stream stream, byte[] buffer, CancellationToken ct) {
        int offset = 0;
        while (offset < buffer.Length) {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0) return false;
            offset += read;
        }

        return true;
    }
}
