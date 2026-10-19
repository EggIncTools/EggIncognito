using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class SpriteAtlas {
    public const string IndexName = "sprites";
    private const int NameBytes = 512;
    private const int EntryBytes = NameBytes + 12;

    public readonly record struct Entry(string Name, float U, float V, float Size);

    public static IReadOnlyList<Entry> ReadIndex(byte[] rpa) {
        if (rpa is null || rpa.Length < 4) return [];
        int count = BinaryPrimitives.ReadInt32LittleEndian(rpa);
        if (count <= 0 || 4 + (long)count * EntryBytes > rpa.Length) return [];
        var entries = new List<Entry>(count);
        for (int i = 0; i < count; i++) {
            int o = 4 + i * EntryBytes;
            int len = Array.IndexOf(rpa, (byte)0, o, NameBytes);
            if (len < 0) len = o + NameBytes;
            string name = Encoding.ASCII.GetString(rpa, o, len - o);
            float u = BinaryPrimitives.ReadSingleLittleEndian(rpa.AsSpan(o + NameBytes));
            float v = BinaryPrimitives.ReadSingleLittleEndian(rpa.AsSpan(o + NameBytes + 4));
            float size = BinaryPrimitives.ReadSingleLittleEndian(rpa.AsSpan(o + NameBytes + 8));
            if (name.Length > 0 && size > 0) entries.Add(new Entry(name, u, v, size));
        }

        return entries;
    }

    public static IReadOnlyList<(string Name, byte[] Png)> Slice(byte[] rpa, byte[] atlasPng) {
        var entries = ReadIndex(rpa);
        if (entries.Count == 0) return [];
        using var atlas = SKBitmap.Decode(atlasPng);
        if (atlas is null) return [];
        var outp = new List<(string, byte[])>(entries.Count);
        foreach (var e in entries) {
            int x = (int)MathF.Round(e.U * atlas.Width);
            int y = (int)MathF.Round(e.V * atlas.Height);
            int w = (int)MathF.Round(e.Size * atlas.Width);
            int h = (int)MathF.Round(e.Size * atlas.Height);
            if (w <= 0 || h <= 0 || x < 0 || y < 0 || x + w > atlas.Width || y + h > atlas.Height) continue;
            using var tile = new SKBitmap(w, h, atlas.ColorType, atlas.AlphaType);
            if (!atlas.ExtractSubset(tile, SKRectI.Create(x, y, w, h))) continue;
            using var image = SKImage.FromBitmap(tile);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            outp.Add((e.Name, png.ToArray()));
        }

        return outp;
    }
}
