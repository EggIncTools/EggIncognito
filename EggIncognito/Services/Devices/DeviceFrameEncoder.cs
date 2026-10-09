using SkiaSharp;

namespace EggIncognito.Services.Devices;

public static class DeviceFrameEncoder {
    public const int DefaultQuality = 75;
    public const int MinQuality = 30;
    public const int MaxQuality = 95;

    public static int ClampQuality(int quality) => Math.Clamp(quality, MinQuality, MaxQuality);

    public static byte[]? ToJpeg(byte[] source, int quality) {
        using var image = SKImage.FromEncodedData(source);
        using var jpeg = image?.Encode(SKEncodedImageFormat.Jpeg, ClampQuality(quality));
        return jpeg?.ToArray();
    }
}
