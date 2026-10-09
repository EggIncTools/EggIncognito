using SkiaSharp;

namespace EggIncognito.Tests.Devices;

internal static class TestPng {
    public static byte[] Make(int size, Action<SKBitmap>? paint = null) {
        using var image = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        image.Erase(new SKColor(10, 10, 10));
        paint?.Invoke(image);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
