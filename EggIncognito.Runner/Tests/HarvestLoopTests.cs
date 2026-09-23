using EggIncognito.Runner.Harvest;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class HarvestLoopTests {
    private static void Ignore(Exception _) { }

    [Fact]
    public async Task ConcurrentPokes_CollapseIntoAtMostTwoPasses() {
        var loop = new HarvestLoop();
        var release = new SemaphoreSlim(0, 1);
        var entered = new SemaphoreSlim(0, 16);
        int passes = 0;

        loop.Poke(false, async _ => {
            int n = Interlocked.Increment(ref passes);
            entered.Release();
            if (n == 1) await release.WaitAsync();
        }, Ignore);

        await entered.WaitAsync();
        for (int i = 0; i < 25; i++) loop.Poke(false, _ => Task.CompletedTask, Ignore);

        Assert.True(loop.Queued);
        release.Release();
        await loop.Idle;

        Assert.Equal(2, passes);
        Assert.False(loop.Running);
        Assert.False(loop.Queued);
    }

    [Fact]
    public async Task ForceFromAQueuedPoke_ReachesTheNextPass() {
        var loop = new HarvestLoop();
        var release = new SemaphoreSlim(0, 1);
        var entered = new SemaphoreSlim(0, 4);
        var seen = new List<bool>();

        loop.Poke(false, async force => {
            lock (seen) seen.Add(force);
            entered.Release();
            if (seen.Count == 1) await release.WaitAsync();
        }, Ignore);

        await entered.WaitAsync();
        loop.Poke(true, _ => Task.CompletedTask, Ignore);
        release.Release();
        await loop.Idle;

        Assert.Equal([false, true], seen);
    }

    [Fact]
    public async Task AFailedPass_DoesNotWedgeTheLoop() {
        var loop = new HarvestLoop();
        var errors = new List<Exception>();
        loop.Poke(false, _ => throw new InvalidOperationException("boom"), e => {
            lock (errors) errors.Add(e);
        });

        await loop.Idle;
        Assert.Single(errors);
        Assert.False(loop.Running);

        int passes = 0;
        loop.Poke(false, _ => {
            Interlocked.Increment(ref passes);
            return Task.CompletedTask;
        }, Ignore);
        await loop.Idle;
        Assert.Equal(1, passes);
    }
}
