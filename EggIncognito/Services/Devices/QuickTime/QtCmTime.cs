using System.Buffers.Binary;
using System.Diagnostics;

namespace EggIncognito.Services.Devices.QuickTime;

internal readonly record struct QtCmTime(ulong Value, uint Scale, uint Flags, ulong Epoch) {
    public const uint NanoSecondScale = 1_000_000_000;
    public const uint FlagHasBeenRounded = 0x1;
    public const int Size = 24;

    public static QtCmTime Read(ReadOnlySpan<byte> src) => new(
        BinaryPrimitives.ReadUInt64LittleEndian(src),
        BinaryPrimitives.ReadUInt32LittleEndian(src[8..]),
        BinaryPrimitives.ReadUInt32LittleEndian(src[12..]),
        BinaryPrimitives.ReadUInt64LittleEndian(src[16..]));

    public void Write(Span<byte> dst) {
        BinaryPrimitives.WriteUInt64LittleEndian(dst, Value);
        BinaryPrimitives.WriteUInt32LittleEndian(dst[8..], Scale);
        BinaryPrimitives.WriteUInt32LittleEndian(dst[12..], Flags);
        BinaryPrimitives.WriteUInt64LittleEndian(dst[16..], Epoch);
    }
}

internal sealed class QtClock(ulong clockRef) {
    private readonly long _started = Stopwatch.GetTimestamp();

    public ulong ClockRef { get; } = clockRef;

    public QtCmTime Now() => new(
        (ulong)Stopwatch.GetElapsedTime(_started).TotalNanoseconds,
        QtCmTime.NanoSecondScale, QtCmTime.FlagHasBeenRounded, 0);
}
