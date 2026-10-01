using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class ApkVersionCode {
    private const ushort ResXmlType = 0x0003;
    private const ushort ResStringPoolType = 0x0001;
    private const ushort ResXmlStartElementType = 0x0102;

    private const byte TypeIntDec = 0x10;
    private const byte TypeString = 0x03;

    public static string? Read(byte[] apkZipBytes) {
        if (apkZipBytes is null || apkZipBytes.Length == 0) return null;
        try {
            using var ms = new MemoryStream(apkZipBytes, false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var entry = zip.GetEntry("AndroidManifest.xml");
            if (entry is null) return null;
            using var es = entry.Open();
            using var buf = new MemoryStream();
            es.CopyTo(buf);
            return ParseAxml(buf.ToArray());
        } catch {
            return null;
        }
    }

    internal static string? ParseAxml(byte[] data) {
        try {
            if (data.Length < 8) return null;
            ushort fileType = ReadU16(data, 0);
            if (fileType != ResXmlType) return null;

            int pos = 8;
            string[]? strings = null;

            while (pos + 8 <= data.Length) {
                ushort type = ReadU16(data, pos);
                ushort headerSize = ReadU16(data, pos + 2);
                int size = (int)ReadU32(data, pos + 4);
                if (size < 8 || pos + size > data.Length) break;

                if (type == ResStringPoolType) {
                    strings = ReadStringPool(data, pos);
                } else if (type == ResXmlStartElementType && strings is not null) {
                    string? code = ReadStartElementVersionCode(data, pos, headerSize, strings);
                    if (code is not null) return code;
                }

                pos += size;
            }

            return null;
        } catch {
            return null;
        }
    }

    public static string? ReadVersionName(byte[] data) => ReadStringAttr(data, "versionName");

    public static string? ReadStringAttr(byte[] data, string attr) {
        try {
            if (data is null || data.Length < 8 || ReadU16(data, 0) != ResXmlType) return null;
            int pos = 8;
            string[]? strings = null;
            while (pos + 8 <= data.Length) {
                ushort type = ReadU16(data, pos);
                ushort headerSize = ReadU16(data, pos + 2);
                int size = (int)ReadU32(data, pos + 4);
                if (size < 8 || pos + size > data.Length) break;
                if (type == ResStringPoolType) {
                    strings = ReadStringPool(data, pos);
                } else if (type == ResXmlStartElementType && strings is not null) {
                    string? value = ReadStartElementStringAttr(data, pos, headerSize, strings, attr);
                    if (value is not null) return value;
                }

                pos += size;
            }

            return null;
        } catch {
            return null;
        }
    }

    private static string? ReadStartElementStringAttr(byte[] data, int chunkPos, int headerSize, string[] strings,
        string attr) {
        int ext = chunkPos + headerSize;
        if (ext + 20 > data.Length) return null;
        ushort attrStart = ReadU16(data, ext + 8);
        ushort attrCount = ReadU16(data, ext + 12);
        int baseAttr = ext + attrStart;
        const int attrRecordSize = 20;
        for (int a = 0; a < attrCount; a++) {
            int rec = baseAttr + a * attrRecordSize;
            if (rec + attrRecordSize > data.Length) break;
            int nameIdx = (int)ReadU32(data, rec + 4);
            string? name = nameIdx >= 0 && nameIdx < strings.Length ? strings[nameIdx] : null;
            if (name != attr) continue;
            int rawValueIdx = (int)ReadU32(data, rec + 8);
            if (rawValueIdx >= 0 && rawValueIdx < strings.Length)
                return strings[rawValueIdx];
            byte dataType = (byte)((ReadU32(data, rec + 12) >> 24) & 0xFF);
            int typedIdx = (int)ReadU32(data, rec + 16);
            if (dataType == TypeString && typedIdx >= 0 && typedIdx < strings.Length)
                return strings[typedIdx];
            return null;
        }

        return null;
    }

    private static string? ReadStartElementVersionCode(byte[] data, int chunkPos, int headerSize, string[] strings) {
        int ext = chunkPos + headerSize;
        if (ext + 20 > data.Length) return null;
        ushort attrStart = ReadU16(data, ext + 8);
        ushort attrCount = ReadU16(data, ext + 12);

        int baseAttr = ext + attrStart;
        const int attrRecordSize = 20;
        for (int a = 0; a < attrCount; a++) {
            int rec = baseAttr + a * attrRecordSize;
            if (rec + attrRecordSize > data.Length) break;
            int nameIdx = (int)ReadU32(data, rec + 4);
            uint typedValue = ReadU32(data, rec + 12);
            byte dataType = (byte)((typedValue >> 24) & 0xFF);
            uint dataVal = ReadU32(data, rec + 16);

            string? name = nameIdx >= 0 && nameIdx < strings.Length ? strings[nameIdx] : null;
            if (name == "versionCode" && dataType == TypeIntDec)
                return dataVal.ToString(CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static string[] ReadStringPool(byte[] data, int chunkPos) {
        if (chunkPos < 0 || chunkPos + 28 > data.Length) return [];
        int stringCount = (int)ReadU32(data, chunkPos + 8);
        uint flags = ReadU32(data, chunkPos + 16);
        int stringsStart = (int)ReadU32(data, chunkPos + 20);
        bool isUtf8 = (flags & 0x100) != 0;

        int offsetsBase = chunkPos + 28;
        if (stringCount < 0 || (long)offsetsBase + (long)stringCount * 4 > data.Length) return [];

        string[] result = new string[stringCount];
        int dataBase = chunkPos + stringsStart;

        for (int i = 0; i < stringCount; i++) {
            int off = (int)ReadU32(data, offsetsBase + i * 4);
            long strPos = (long)dataBase + off;
            if (strPos < 0 || strPos >= data.Length) {
                result[i] = "";
                continue;
            }

            result[i] = isUtf8 ? ReadUtf8String(data, (int)strPos) : ReadUtf16String(data, (int)strPos);
        }

        return result;
    }

    private static string ReadUtf8String(byte[] data, int pos) {
        int p = pos;
        p = SkipUtf8Len(data, p);
        (int byteLen, int next) = ReadUtf8Len(data, p);
        return Encoding.UTF8.GetString(data, next, byteLen);
    }

    private static int SkipUtf8Len(byte[] data, int pos) {
        (_, int next) = ReadUtf8Len(data, pos);
        return next;
    }

    private static (int Len, int Next) ReadUtf8Len(byte[] data, int pos) {
        int b = data[pos];
        if ((b & 0x80) != 0)
            return (((b & 0x7F) << 8) | data[pos + 1], pos + 2);
        return (b, pos + 1);
    }

    private static string ReadUtf16String(byte[] data, int pos) {
        int len = ReadU16(data, pos);
        int p = pos + 2;
        if ((len & 0x8000) != 0) {
            len = ((len & 0x7FFF) << 16) | ReadU16(data, p);
            p += 2;
        }

        return Encoding.Unicode.GetString(data, p, len * 2);
    }

    private static ushort ReadU16(byte[] data, int pos) => (ushort)(data[pos] | (data[pos + 1] << 8));

    private static uint ReadU32(byte[] data, int pos) =>
        (uint)(data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24));
}
