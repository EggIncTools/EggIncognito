using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class DeviceCookbooksDescribeTests {
    private static readonly DeviceTarget Target = new("dev", Platforms.Android, "emulator-5554", "com.auxbrain.egg");

    private sealed class Book(string id, Func<CancellationToken, Task<DeviceCookbookInfo>> describe) : IDeviceCookbook {
        public string Id => id;
        public string Title => id;
        public string Summary => "";

        public Task<DeviceCookbookInfo> DescribeAsync(DeviceTarget target, CancellationToken ct) => describe(ct);

        public Task<DeviceCookbookRun> RunAsync(DeviceCookbookContext context, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private static Book Ready(string id) =>
        new(id, _ => Task.FromResult(new DeviceCookbookInfo(id, id, "", true)));

    [Fact]
    public async Task HangingDescribe_ComesBackUnavailableWithinBudget() {
        var hung = new Book("hung", _ => new TaskCompletionSource<DeviceCookbookInfo>().Task);

        var info = await DeviceCookbooks.DescribeBoundedAsync(hung, Target, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.False(info.Available);
        Assert.Equal("hung", info.Id);
        Assert.Contains("took longer", info.Unavailable);
    }

    [Fact]
    public async Task ThrowingDescribe_ComesBackUnavailable() {
        var broken = new Book("broken", _ => throw new InvalidOperationException("boom"));

        var info = await DeviceCookbooks.DescribeBoundedAsync(broken, Target, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.False(info.Available);
        Assert.Contains("boom", info.Unavailable);
    }

    [Fact]
    public async Task DescribeAll_KeepsIdOrder() {
        var all = new DeviceCookbooks([Ready("b"), Ready("a"), Ready("c")]);

        var described = await all.DescribeAllAsync(Target, CancellationToken.None);

        Assert.Equal(["a", "b", "c"], described.Select(d => d.Id));
        Assert.All(described, d => Assert.True(d.Available));
    }

    [Fact]
    public async Task CallerCancellation_Propagates() {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var slow = new Book("slow", async ct => {
            await Task.Delay(Timeout.Infinite, ct);
            return new DeviceCookbookInfo("slow", "slow", "", true);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DeviceCookbooks.DescribeBoundedAsync(slow, Target, TimeSpan.FromSeconds(5), cts.Token));
    }
}
