using System.Buffers.Binary;

namespace EggIncognito.Services.Devices.QuickTime;

internal static class QtMagic {
    public const uint Ping = 0x70696E67;
    public const uint Sync = 0x73796E63;
    public const uint Asyn = 0x6173796E;
    public const uint Reply = 0x72706C79;

    public const uint Time = 0x74696D65;
    public const uint Cwpa = 0x63777061;
    public const uint Afmt = 0x61666D74;
    public const uint Cvrp = 0x63767270;
    public const uint Clok = 0x636C6F6B;
    public const uint Go = 0x676F2120;
    public const uint Skew = 0x736B6577;
    public const uint Stop = 0x73746F70;

    public const uint Feed = 0x66656564;
    public const uint Eat = 0x65617421;
    public const uint Sprp = 0x73707270;
    public const uint Srat = 0x73726174;
    public const uint Tbas = 0x74626173;
    public const uint Tjmp = 0x746A6D70;
    public const uint Rels = 0x72656C73;
    public const uint Hpd1 = 0x68706431;
    public const uint Hpa1 = 0x68706131;
    public const uint Hpd0 = 0x68706430;
    public const uint Hpa0 = 0x68706130;
    public const uint Need = 0x6E656564;

    public const uint Sbuf = 0x73627566;
    public const uint Opts = 0x6F707473;
    public const uint Stia = 0x73746961;
    public const uint Sdat = 0x73646174;
    public const uint Nsmp = 0x6E736D70;
    public const uint Ssiz = 0x7373697A;
    public const uint Fdsc = 0x66647363;
    public const uint Satt = 0x73617474;
    public const uint Sary = 0x73617279;

    public const uint Dict = 0x64696374;
    public const uint Keyv = 0x6B657976;
    public const uint Strk = 0x7374726B;
    public const uint Idxk = 0x6964786B;
    public const uint Strv = 0x73747276;
    public const uint Datv = 0x64617476;
    public const uint Bulv = 0x62756C76;
    public const uint Nmbv = 0x6E6D6276;

    public const uint Mdia = 0x6D646961;
    public const uint Vdim = 0x7664696D;
    public const uint Codc = 0x636F6463;
    public const uint Extn = 0x6578746E;
    public const uint Vide = 0x76696465;
}

internal static class QtPackets {
    public const ulong EmptyCFType = 0x1;
    private const ulong PingHeader = 0x0000000100000000;

    public static byte[] Ping() {
        byte[] p = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(p, 16);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), QtMagic.Ping);
        BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(8), PingHeader);
        return p;
    }

    public static byte[] ClockRefReply(ulong clockRef, ulong correlationId) {
        byte[] p = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(p, 28);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), QtMagic.Reply);
        BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(8), correlationId);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(16), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(20), clockRef);
        return p;
    }

    public static byte[] EmptyReply(ulong correlationId) {
        byte[] p = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(p, 24);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), QtMagic.Reply);
        BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(8), correlationId);
        BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(16), 0);
        return p;
    }

    public static byte[] TimeReply(ulong correlationId, QtCmTime time) {
        byte[] p = new byte[44];
        BinaryPrimitives.WriteUInt32LittleEndian(p, 44);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), QtMagic.Reply);
        BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(8), correlationId);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(16), 0);
        time.Write(p.AsSpan(20));
        return p;
    }

    public static byte[] SkewReply(ulong correlationId, double skew) {
        byte[] p = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(p, 28);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), QtMagic.Reply);
        BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(8), correlationId);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(16), 0);
        BinaryPrimitives.WriteDoubleLittleEndian(p.AsSpan(20), skew);
        return p;
    }

    public static byte[] AfmtReply(ulong correlationId) {
        byte[] dict = QtDict.ErrorZero();
        byte[] p = new byte[20 + dict.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(p, (uint)p.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), QtMagic.Reply);
        BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(8), correlationId);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(16), 0);
        dict.CopyTo(p.AsSpan(20));
        return p;
    }

    public static byte[] Asyn(ulong clockRef, uint subtype) {
        byte[] p = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(p, 20);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), QtMagic.Asyn);
        BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(8), clockRef);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(16), subtype);
        return p;
    }

    public static byte[] Need(ulong deviceVideoClockRef) => Asyn(deviceVideoClockRef, QtMagic.Need);
    public static byte[] Hpd0() => Asyn(EmptyCFType, QtMagic.Hpd0);
    public static byte[] Hpa0(ulong deviceAudioClockRef) => Asyn(deviceAudioClockRef, QtMagic.Hpa0);

    public static byte[] AsynDict(ulong clockRef, uint subtype, byte[] dict) {
        byte[] p = new byte[20 + dict.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(p, (uint)p.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), QtMagic.Asyn);
        BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(8), clockRef);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(16), subtype);
        dict.CopyTo(p.AsSpan(20));
        return p;
    }

    public static byte[] Hpd1(int width, int height) =>
        AsynDict(EmptyCFType, QtMagic.Hpd1, QtDict.Hpd1(width, height));

    public static byte[] Hpa1(ulong deviceAudioClockRef) =>
        AsynDict(deviceAudioClockRef, QtMagic.Hpa1, QtDict.Hpa1());
}
