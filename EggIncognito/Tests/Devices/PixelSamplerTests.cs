using EggIncognito.Services.Devices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace EggIncognito.Tests.Devices;

public class PixelSamplerTests {
    private static byte[] Png(Action<Image<Rgba32>> paint) {
        using var image = new Image<Rgba32>(4, 4, new Rgba32(10, 10, 10));
        paint(image);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Sample_ReadsTheExactPixel() {
        var png = Png(i => i[2, 1] = new Rgba32(30, 90, 220));

        var px = PixelSampler.Sample(png, 2, 1);

        Assert.NotNull(px);
        Assert.Equal("#1e5adc", PixelSampler.Hex(px.Value));
        Assert.Equal("#0a0a0a", PixelSampler.Hex(PixelSampler.Sample(png, 0, 0)!.Value));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    public void Sample_OutsideTheImage_IsNull(int x, int y) {
        Assert.Null(PixelSampler.Sample(Png(_ => { }), x, y));
    }

    [Fact]
    public void Sample_BrokenBytes_IsNull() {
        Assert.Null(PixelSampler.Sample([1, 2, 3], 0, 0));
    }

    [Fact]
    public void SampleMany_ReadsEveryPointFromOneDecode() {
        var png = Png(i => {
            i[2, 1] = new Rgba32(30, 90, 220);
            i[0, 3] = new Rgba32(200, 10, 10);
        });

        var hits = PixelSampler.SampleMany(png, [(2, 1), (0, 3), (1, 1)]);

        Assert.Equal(3, hits.Length);
        Assert.Equal("#1e5adc", PixelSampler.Hex(hits[0]!.Value));
        Assert.Equal("#c80a0a", PixelSampler.Hex(hits[1]!.Value));
        Assert.Equal("#0a0a0a", PixelSampler.Hex(hits[2]!.Value));
    }

    [Fact]
    public void SampleMany_OutOfRangePointsAreNullAndDoNotHideTheRest() {
        var png = Png(i => i[3, 3] = new Rgba32(1, 2, 3));

        var hits = PixelSampler.SampleMany(png, [(-1, 0), (3, 3), (4, 0), (0, 4)]);

        Assert.Null(hits[0]);
        Assert.Equal("#010203", PixelSampler.Hex(hits[1]!.Value));
        Assert.Null(hits[2]);
        Assert.Null(hits[3]);
    }

    [Fact]
    public void SampleMany_BrokenBytes_AreAllNull() {
        var hits = PixelSampler.SampleMany([1, 2, 3], [(0, 0), (1, 1)]);

        Assert.Equal(2, hits.Length);
        Assert.All(hits, Assert.Null);
    }

    [Fact]
    public void SampleMany_NoPoints_NeverDecodes() {
        Assert.Empty(PixelSampler.SampleMany([1, 2, 3], []));
    }

    [Fact]
    public void Close_UsesPerChannelTolerance() {
        var blue = new Rgba32(30, 90, 220);
        Assert.True(PixelSampler.Close(blue, new Rgba32(60, 60, 200), 48));
        Assert.False(PixelSampler.Close(blue, new Rgba32(30, 150, 220), 48));
    }
}
