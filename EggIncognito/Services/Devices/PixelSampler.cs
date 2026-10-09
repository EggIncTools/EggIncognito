using SkiaSharp;

namespace EggIncognito.Services.Devices;

public static class PixelSampler {
    public static SKColor?[] SampleMany(byte[] png, IReadOnlyList<(int X, int Y)> points) {
        var hits = new SKColor?[points.Count];
        if (points.Count == 0) return hits;
        using var data = SKData.CreateCopy(png);
        using var codec = SKCodec.Create(data);
        if (codec is null) return hits;
        using var image = SKBitmap.Decode(codec);
        for (int i = 0; i < points.Count; i++) {
            (int x, int y) = points[i];
            if (x < 0 || y < 0 || x >= image.Width || y >= image.Height) continue;
            hits[i] = image.GetPixel(x, y);
        }
        return hits;
    }

    public static SKColor? Sample(byte[] png, int x, int y) => SampleMany(png, [(X: x, Y: y)])[0];

    public static bool Close(SKColor a, SKColor b, int tolerance) =>
        Math.Abs(a.Red - b.Red) <= tolerance && Math.Abs(a.Green - b.Green) <= tolerance && Math.Abs(a.Blue - b.Blue) <= tolerance;

    public static string Hex(SKColor c) => $"#{c.Red:x2}{c.Green:x2}{c.Blue:x2}";
}
