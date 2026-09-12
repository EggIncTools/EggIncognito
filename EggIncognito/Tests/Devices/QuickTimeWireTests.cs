using System.Buffers.Binary;
using EggIncognito.Services.Devices.QuickTime;

namespace EggIncognito.Tests.Devices;

public class QuickTimeWireTests {
    private static readonly byte[] RealAvcC = [
        0x01, 0x64, 0x00, 0x33, 0xFF, 0xE1, 0x00, 0x11,
        0x27, 0x64, 0x00, 0x33, 0xAC, 0x56, 0x80, 0x47, 0x01, 0x33, 0xE6, 0x9E, 0x6E, 0x04, 0x04, 0x04, 0x04,
        0x01, 0x00, 0x04,
        0x28, 0xEE, 0x3C, 0xB0,
        0xFD, 0xF8, 0xF8, 0x00
    ];

    [Fact]
    public void ParameterSets_FromRealAvcC_AreSpsThenPpsNotSwapped() {
        Assert.True(QtSampleBuffer.TryReadParameterSets(RealAvcC, out byte[] sps, out byte[] pps));

        Assert.Equal(17, sps.Length);
        Assert.Equal(7, sps[0] & 0x1F);
        Assert.Equal(4, pps.Length);
        Assert.Equal(8, pps[0] & 0x1F);
    }

    [Fact]
    public void LengthPrefixSize_FromRealAvcC_IsFour() =>
        Assert.Equal(4, QtSampleBuffer.LengthPrefixSize(RealAvcC));

    [Fact]
    public void SplitNalus_BigEndianPrefixes_SplitEveryNaluNotJustTheFirst() {
        byte[] sdat = new byte[4 + 3 + 4 + 5];
        BinaryPrimitives.WriteUInt32BigEndian(sdat, 3);
        sdat[4] = 0x06;
        BinaryPrimitives.WriteUInt32BigEndian(sdat.AsSpan(7), 5);
        sdat[11] = 0x25;

        var nalus = QtSampleBuffer.SplitNalus(sdat, 4);

        Assert.Equal(2, nalus.Count);
        Assert.Equal(3, nalus[0].Length);
        Assert.Equal(6, nalus[0][0] & 0x1F);
        Assert.Equal(5, nalus[1].Length);
        Assert.Equal(5, nalus[1][0] & 0x1F);
    }

    [Fact]
    public void SplitNalus_TruncatedTrailingNalu_StopsInsteadOfOverrunning() {
        byte[] sdat = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(sdat, 99);

        Assert.Empty(QtSampleBuffer.SplitNalus(sdat, 4));
    }

    [Fact]
    public void Ping_MatchesTheSixteenByteConstantOnTheWire() {
        byte[] ping = QtPackets.Ping();

        Assert.Equal(16, ping.Length);
        Assert.Equal<byte[]>(
            [0x10, 0x00, 0x00, 0x00, 0x67, 0x6E, 0x69, 0x70, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00],
            ping);
    }

    [Fact]
    public void ClockRefReply_EchoesCorrelationIdAndCarriesClockRef() {
        byte[] reply = QtPackets.ClockRefReply(0x00007FA66CE20CB0, 0x000000011357_3DE0);

        Assert.Equal(28u, BinaryPrimitives.ReadUInt32LittleEndian(reply));
        Assert.Equal(0x72706C79u, BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(4)));
        Assert.Equal(0x0000000113573DE0ul, BinaryPrimitives.ReadUInt64LittleEndian(reply.AsSpan(8)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(16)));
        Assert.Equal(0x00007FA66CE20CB0ul, BinaryPrimitives.ReadUInt64LittleEndian(reply.AsSpan(20)));
    }

    [Fact]
    public void Need_CarriesTheDeviceVideoClockRefAndTheNeedSubtype() {
        byte[] need = QtPackets.Need(0xDEADBEEFul);

        Assert.Equal(20, need.Length);
        Assert.Equal(0x6173796Eu, BinaryPrimitives.ReadUInt32LittleEndian(need.AsSpan(4)));
        Assert.Equal(0xDEADBEEFul, BinaryPrimitives.ReadUInt64LittleEndian(need.AsSpan(8)));
        Assert.Equal(0x6E656564u, BinaryPrimitives.ReadUInt32LittleEndian(need.AsSpan(16)));
    }

    [Fact]
    public void Hpd0_UsesEmptyCFTypeWhileHpa0UsesTheDeviceAudioClock() {
        Assert.Equal(1ul, BinaryPrimitives.ReadUInt64LittleEndian(QtPackets.Hpd0().AsSpan(8)));
        Assert.Equal(0x1234ul, BinaryPrimitives.ReadUInt64LittleEndian(QtPackets.Hpa0(0x1234).AsSpan(8)));
    }

    [Fact]
    public void CmTime_RoundTripsThroughTwentyFourBytes() {
        var time = new QtCmTime(0xE1E142C462BA0000, 0x3B9ACA00, 0x1, 0);
        byte[] buf = new byte[QtCmTime.Size];

        time.Write(buf);

        Assert.Equal(time, QtCmTime.Read(buf));
    }

    [Fact]
    public void Asbd_IsFiftySixBytesWithLpcmAtFortyEightKilohertz() {
        byte[] asbd = QtAudio.Asbd();

        Assert.Equal(56, asbd.Length);
        Assert.Equal(48000.0, BinaryPrimitives.ReadDoubleLittleEndian(asbd));
        Assert.Equal(0x6C70636Du, BinaryPrimitives.ReadUInt32LittleEndian(asbd.AsSpan(8)));
    }
}
