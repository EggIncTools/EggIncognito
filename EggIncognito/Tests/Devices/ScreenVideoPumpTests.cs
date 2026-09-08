using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class ScreenVideoPumpTests {
    private static ProcessHandle Segment(byte[] bytes, string stderr = "", int exit = 0) =>
        new(new MemoryStream(bytes), Task.FromResult(exit), () => stderr, () => ValueTask.CompletedTask);

    private static Func<CancellationToken, Task<ProcessHandle?>> Factory(params ProcessHandle?[] segments) {
        int index = 0;
        return _ => Task.FromResult(index < segments.Length ? segments[index++] : null);
    }

    [Theory]
    [InlineData(1080, 2340, 720, 1280, "592x1280")]
    [InlineData(1080, 1920, 720, 1280, "720x1280")]
    [InlineData(1080, 2340, 1080, 1920, "888x1920")]
    [InlineData(720, 1280, 1080, 1920, "720x1280")]
    [InlineData(0, 0, 720, 1280, "720x1280")]
    public void FitSize_PreservesDisplayAspect_AlignedToEight(int dw, int dh, int bw, int bh, string expected) {
        Assert.Equal(expected, ScreenVideoPump.FitSize(dw, dh, bw, bh));
    }

    [Fact]
    public async Task RunAsync_TwoSegments_ArriveInOrderThenStopsOnNullFactory() {
        byte[] first = [0, 0, 0, 1, 0x67, 0xAA];
        byte[] second = [0, 0, 0, 1, 0x67, 0xBB, 0xCC];
        var output = new MemoryStream();
        var pump = new ScreenVideoPump(Factory(Segment(first), Segment(second)));

        string? note = await pump.RunAsync(output, CancellationToken.None);

        byte[] expected = [.. first, .. second];
        Assert.Equal(expected, output.ToArray());
        Assert.NotNull(note);
    }

    [Fact]
    public async Task RunAsync_FactoryReturnsNull_StopsWithNote() {
        var output = new MemoryStream();
        var pump = new ScreenVideoPump(Factory());

        string? note = await pump.RunAsync(output, CancellationToken.None);

        Assert.Contains("screenrecord", note, StringComparison.Ordinal);
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task RunAsync_ThreeInstantSegments_StopsWithStderrTail() {
        int started = 0;
        var pump = new ScreenVideoPump(_ => {
            started++;
            return Task.FromResult<ProcessHandle?>(Segment([1], "screenrecord: unable to configure codec", 1));
        });

        string? note = await pump.RunAsync(new MemoryStream(), CancellationToken.None);

        Assert.Equal(ScreenVideoPump.InstantSegmentLimit, started);
        Assert.Contains("unable to configure codec", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Cancelled_ReturnsNull() {
        using var cts = new CancellationTokenSource();
        var pump = new ScreenVideoPump(_ => {
            cts.Cancel();
            return Task.FromResult<ProcessHandle?>(Segment([1, 2, 3]));
        });

        string? note = await pump.RunAsync(new MemoryStream(), cts.Token);

        Assert.Null(note);
    }
}
