using System.Text;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class BridgeStreamFramesTests {
    [Fact]
    public async Task WriteThenRead_RoundTripsStdoutStderrAndExit() {
        using var stream = new MemoryStream();
        await BridgeStreamFrames.WriteAsync(stream, BridgeStreamFrames.Stdout, "frame one"u8.ToArray(),
            CancellationToken.None);
        await BridgeStreamFrames.WriteAsync(stream, BridgeStreamFrames.Stderr, "bad news"u8.ToArray(),
            CancellationToken.None);
        await BridgeStreamFrames.WriteExitAsync(stream, 7, CancellationToken.None);
        stream.Position = 0;

        var first = await BridgeStreamFrames.ReadAsync(stream, CancellationToken.None);
        var second = await BridgeStreamFrames.ReadAsync(stream, CancellationToken.None);
        var third = await BridgeStreamFrames.ReadAsync(stream, CancellationToken.None);

        Assert.Equal(BridgeStreamFrames.Stdout, first!.Value.Kind);
        Assert.Equal("frame one", Encoding.UTF8.GetString(first.Value.Payload));
        Assert.Equal(BridgeStreamFrames.Stderr, second!.Value.Kind);
        Assert.Equal("bad news", Encoding.UTF8.GetString(second.Value.Payload));
        Assert.Equal(BridgeStreamFrames.Exit, third!.Value.Kind);
        Assert.Equal(7, third.Value.ExitCode);
    }

    [Fact]
    public async Task ReadAsync_EmptyStream_ReturnsNull() {
        using var stream = new MemoryStream();

        Assert.Null(await BridgeStreamFrames.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_TruncatedPayload_ReturnsNull() {
        using var buffer = new MemoryStream();
        await BridgeStreamFrames.WriteAsync(buffer, BridgeStreamFrames.Stdout, new byte[64], CancellationToken.None);
        byte[] whole = buffer.ToArray();

        using var truncated = new MemoryStream(whole, 0, whole.Length - 10);

        Assert.Null(await BridgeStreamFrames.ReadAsync(truncated, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_TruncatedHeader_ReturnsNull() {
        using var truncated = new MemoryStream([BridgeStreamFrames.Exit, 0, 0]);

        Assert.Null(await BridgeStreamFrames.ReadAsync(truncated, CancellationToken.None));
    }
}
