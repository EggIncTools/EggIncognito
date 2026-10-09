using SkiaSharp;

namespace EggIncognito.Services.Assets;

public static class EventIconRenderer {
    public static byte[] Render(byte[] glyphPng, string eventType, bool ccOnly) {
        using var glyph = SKImage.FromEncodedData(glyphPng) ?? throw new InvalidDataException("event glyph is not a decodable image");
        var hex = EventPalette.ColorFor(eventType);
        var newWidth = (int)(glyph.Width * 1.1);
        var newHeight = (int)(glyph.Height * 1.1);
        using var surface = SKSurface.Create(new SKImageInfo(newWidth, newHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using (var fill = new SKPaint()) {
            if (ccOnly) {
                fill.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(newWidth, 0),
                    [SKColor.Parse(EventPalette.CcGradientFrom), SKColor.Parse(EventPalette.CcGradientTo)],
                    SKShaderTileMode.Clamp);
            } else {
                fill.Color = SKColor.Parse(hex);
            }
            canvas.DrawRect(0, 0, newWidth, newHeight, fill);
        }
        canvas.DrawImage(glyph, (newWidth - glyph.Width) / 2, (newHeight - glyph.Height) / 2, SKSamplingOptions.Default);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
