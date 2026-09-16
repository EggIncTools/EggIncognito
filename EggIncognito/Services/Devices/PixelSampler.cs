using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace EggIncognito.Services.Devices;

public static class PixelSampler {
    public static Rgba32?[] SampleMany(byte[] png, IReadOnlyList<(int X, int Y)> points) {
        var hits = new Rgba32?[points.Count];
        if (points.Count == 0) return hits;
        try {
            using var image = Image.Load<Rgba32>(png);
            for (int i = 0; i < points.Count; i++) {
                (int x, int y) = points[i];
                if (x < 0 || y < 0 || x >= image.Width || y >= image.Height) continue;
                hits[i] = image[x, y];
            }
            return hits;
        } catch (Exception ex) when (ex is ImageFormatException or NotSupportedException) {
            return new Rgba32?[points.Count];
        }
    }

    public static Rgba32? Sample(byte[] png, int x, int y) => SampleMany(png, [(X: x, Y: y)])[0];

    public static bool Close(Rgba32 a, Rgba32 b, int tolerance) =>
        Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;

    public static string Hex(Rgba32 c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";
}
