using EggIncognito.Services.Assets;
using EggIncognito.Services.Devices;
using EggIncognito.Tests.Devices;
using SkiaSharp;

namespace EggIncognito.Tests;

public class EventIconRendererTests {
    private static byte[] Glyph() => TestPng.Make(20, i => i.Erase(SKColors.Transparent));

    [Fact]
    public void Render_PadsTheGlyphOnTheEventColour() {
        using var icon = SKBitmap.Decode(EventIconRenderer.Render(Glyph(), "hab-sale", ccOnly: false));

        Assert.Equal(22, icon.Width);
        Assert.Equal(22, icon.Height);
        Assert.Equal(EventPalette.ColorFor("hab-sale"), PixelSampler.Hex(icon.GetPixel(11, 11)));
    }

    [Fact]
    public void Render_CcOnly_RunsTheGradientLeftToRight() {
        using var icon = SKBitmap.Decode(EventIconRenderer.Render(Glyph(), "hab-sale", ccOnly: true));

        var left = icon.GetPixel(0, 11);
        var right = icon.GetPixel(icon.Width - 1, 11);
        Assert.True(PixelSampler.Close(left, SKColor.Parse(EventPalette.CcGradientFrom), 16));
        Assert.True(PixelSampler.Close(right, SKColor.Parse(EventPalette.CcGradientTo), 16));
    }
}
