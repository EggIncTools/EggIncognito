using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace EggIncognito.Services.Devices;

public static class PixelSampler {
    public static Rgba32? Sample(byte[] png, int x, int y) {
        try {
            using var image = Image.Load<Rgba32>(png);
            if (x < 0 || y < 0 || x >= image.Width || y >= image.Height) return null;
            return image[x, y];
        } catch (Exception ex) when (ex is ImageFormatException or NotSupportedException) {
            return null;
        }
    }

    public static bool Close(Rgba32 a, Rgba32 b, int tolerance) =>
        Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;

    public static string Hex(Rgba32 c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";
}
