using System.Buffers.Binary;
using System.Text;
using EggIncognito.Core.Services.ProtoExtract;
using SkiaSharp;

namespace EggIncognito.Tests.ProtoExtract;

public sealed class SpriteAtlasTests {
    private static byte[] Index(params (string Name, float U, float V, float Size)[] entries) {
        var buf = new byte[4 + entries.Length * 524];
        BinaryPrimitives.WriteInt32LittleEndian(buf, entries.Length);
        for (int i = 0; i < entries.Length; i++) {
            int o = 4 + i * 524;
            Encoding.ASCII.GetBytes(entries[i].Name).CopyTo(buf, o);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(o + 512), entries[i].U);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(o + 516), entries[i].V);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(o + 520), entries[i].Size);
        }

        return buf;
    }

    private static byte[] Atlas(int size, SKColor topLeft, SKColor bottomRight) {
        using var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = topLeft };
        canvas.DrawRect(0, 0, size / 2f, size / 2f, paint);
        paint.Color = bottomRight;
        canvas.DrawRect(size / 2f, size / 2f, size / 2f, size / 2f, paint);
        using var image = SKImage.FromBitmap(bmp);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    [Fact]
    public void ReadIndex_decodes_name_and_uv_rect() {
        var entries = SpriteAtlas.ReadIndex(Index(("b_icon_drone_boost", 0.125f, 0.375f, 0.125f)));
        var e = Assert.Single(entries);
        Assert.Equal("b_icon_drone_boost", e.Name);
        Assert.Equal(0.125f, e.U);
        Assert.Equal(0.375f, e.V);
        Assert.Equal(0.125f, e.Size);
    }

    [Fact]
    public void Slice_crops_top_down_from_the_atlas() {
        byte[] atlas = Atlas(64, SKColors.Red, SKColors.Blue);
        var slices = SpriteAtlas.Slice(Index(("a", 0f, 0f, 0.5f), ("b", 0.5f, 0.5f, 0.5f)), atlas);
        Assert.Equal(2, slices.Count);
        using var a = SKBitmap.Decode(slices[0].Png);
        using var b = SKBitmap.Decode(slices[1].Png);
        Assert.Equal(32, a.Width);
        Assert.Equal(SKColors.Red, a.GetPixel(8, 8));
        Assert.Equal(SKColors.Blue, b.GetPixel(8, 8));
    }

    [Fact]
    public void Slice_skips_entries_outside_the_atlas() {
        byte[] atlas = Atlas(16, SKColors.Red, SKColors.Blue);
        var slices = SpriteAtlas.Slice(Index(("ok", 0f, 0f, 0.5f), ("bad", 0.75f, 0.75f, 0.5f)), atlas);
        Assert.Equal("ok", Assert.Single(slices).Name);
    }
}
