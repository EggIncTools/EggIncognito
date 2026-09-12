using System.Buffers.Binary;

namespace EggIncognito.Services.Devices.QuickTime;

internal sealed class QtSession(Func<byte[], CancellationToken, Task> send, Stream output, int width, int height) {
    private const ulong AudioClockOffset = 1000;
    private const ulong VideoClockOffset = 0x1000AF;
    private const ulong HostClockOffset = 0x10000;

    private ulong _deviceAudioClockRef;
    private ulong _deviceVideoClockRef;
    private ulong _hostAudioClockRef;
    private QtClock? _hostClock;
    private byte[]? _need;
    private bool _parameterSetsSent;
    private int _prefixSize = 4;
    private int _releaseCount;

    public bool Released => _releaseCount >= 2;
    public int Frames { get; private set; }

    public async Task HandleAsync(byte[] body, CancellationToken ct) {
        if (body.Length < 4) return;
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(body);
        switch (magic) {
            case QtMagic.Ping:
                await send(QtPackets.Ping(), ct);
                break;
            case QtMagic.Sync:
                await HandleSyncAsync(body, ct);
                break;
            case QtMagic.Asyn:
                await HandleAsynAsync(body, ct);
                break;
        }
    }

    private async Task HandleSyncAsync(byte[] body, CancellationToken ct) {
        if (body.Length < 24) return;
        ulong clockRef = BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(4));
        uint subtype = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12));
        ulong correlationId = BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(16));

        switch (subtype) {
            case QtMagic.Cwpa:
                if (body.Length < 32) return;
                _deviceAudioClockRef = BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(24));
                _hostAudioClockRef = _deviceAudioClockRef + AudioClockOffset;
                await send(QtPackets.Hpd1(width, height), ct);
                await send(QtPackets.Hpd1(width, height), ct);
                await send(QtPackets.ClockRefReply(_hostAudioClockRef, correlationId), ct);
                await send(QtPackets.Hpa1(_deviceAudioClockRef), ct);
                break;
            case QtMagic.Afmt:
                await send(QtPackets.AfmtReply(correlationId), ct);
                break;
            case QtMagic.Cvrp:
                if (body.Length < 32) return;
                _deviceVideoClockRef = BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(24));
                _need = QtPackets.Need(_deviceVideoClockRef);
                await send(_need, ct);
                await send(QtPackets.ClockRefReply(_deviceVideoClockRef + VideoClockOffset, correlationId), ct);
                break;
            case QtMagic.Clok:
                _hostClock = new QtClock(clockRef + HostClockOffset);
                await send(QtPackets.ClockRefReply(_hostClock.ClockRef, correlationId), ct);
                break;
            case QtMagic.Time:
                await send(QtPackets.TimeReply(correlationId, _hostClock?.Now() ?? default), ct);
                break;
            case QtMagic.Skew:
                await send(QtPackets.SkewReply(correlationId, QtAudio.SampleRate), ct);
                break;
            case QtMagic.Go:
            case QtMagic.Stop:
                await send(QtPackets.EmptyReply(correlationId), ct);
                break;
        }
    }

    private async Task HandleAsynAsync(byte[] body, CancellationToken ct) {
        if (body.Length < 16) return;
        uint subtype = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12));
        if (subtype == QtMagic.Rels) {
            _releaseCount++;
            return;
        }
        if (subtype != QtMagic.Feed) return;

        try {
            await WriteFrameAsync(body, ct);
        } finally {
            if (_need is not null) await send(_need, ct);
        }
    }

    private async Task WriteFrameAsync(byte[] body, CancellationToken ct) {
        byte[]? sps = null;
        byte[]? pps = null;

        if (!TryReadFrame(body, ref sps, ref pps, out List<byte[]> nalus)) return;

        if (!_parameterSetsSent && sps is not null && pps is not null) {
            await QtSampleBuffer.WriteAnnexBAsync(output, sps, ct);
            await QtSampleBuffer.WriteAnnexBAsync(output, pps, ct);
            _parameterSetsSent = true;
        }

        if (!_parameterSetsSent) return;

        foreach (byte[] nalu in nalus) {
            await QtSampleBuffer.WriteAnnexBAsync(output, nalu, ct);
        }
        Frames++;
        await output.FlushAsync(ct);
    }

    private bool TryReadFrame(byte[] body, ref byte[]? sps, ref byte[]? pps, out List<byte[]> nalus) {
        nalus = [];
        if (!QtSampleBuffer.TryReadSdat(body.AsSpan(16), out var sdat, out var avcC)) return false;

        if (!avcC.IsEmpty) {
            _prefixSize = QtSampleBuffer.LengthPrefixSize(avcC);
            if (!_parameterSetsSent && QtSampleBuffer.TryReadParameterSets(avcC, out byte[] s, out byte[] p)) {
                sps = s;
                pps = p;
            }
        }

        nalus = QtSampleBuffer.SplitNalus(sdat, _prefixSize);
        return true;
    }

    public async Task CloseAsync(CancellationToken ct) {
        if (_deviceAudioClockRef != 0) await send(QtPackets.Hpa0(_deviceAudioClockRef), ct);
        await send(QtPackets.Hpd0(), ct);
    }
}
