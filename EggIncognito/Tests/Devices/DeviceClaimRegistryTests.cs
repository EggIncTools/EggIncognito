using EggIncognito.Services.Devices;
using Microsoft.Extensions.Time.Testing;

namespace EggIncognito.Tests.Devices;

public class DeviceClaimRegistryTests {
    [Fact]
    public void Claim_SetsHeldTrue_ReturnsNowPlusTtl() {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var registry = new DeviceClaimRegistry(time);

        var expires = registry.Claim("d1", TimeSpan.FromSeconds(60));

        Assert.True(registry.IsHeld("d1"));
        Assert.Equal(time.GetUtcNow() + TimeSpan.FromSeconds(60), expires);
    }

    [Fact]
    public void IsHeld_AfterTtlElapses_FalseAndCleansUp() {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var registry = new DeviceClaimRegistry(time);
        registry.Claim("d1", TimeSpan.FromSeconds(10));

        time.Advance(TimeSpan.FromSeconds(11));

        Assert.False(registry.IsHeld("d1"));
        Assert.False(registry.IsHeld("d1"));
    }

    [Fact]
    public void Release_ClearsHeldImmediately() {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var registry = new DeviceClaimRegistry(time);
        registry.Claim("d1", TimeSpan.FromSeconds(60));

        registry.Release("d1");

        Assert.False(registry.IsHeld("d1"));
    }

    [Fact]
    public void Claim_CalledAgainBeforeExpiry_ExtendsExpiry() {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var registry = new DeviceClaimRegistry(time);
        registry.Claim("d1", TimeSpan.FromSeconds(10));

        time.Advance(TimeSpan.FromSeconds(5));
        var expires = registry.Claim("d1", TimeSpan.FromSeconds(60));

        time.Advance(TimeSpan.FromSeconds(10));

        Assert.True(registry.IsHeld("d1"));
        Assert.Equal(time.GetUtcNow().AddSeconds(50), expires);
    }

    [Fact]
    public void IsHeld_UnknownId_False() {
        var registry = new DeviceClaimRegistry(new FakeTimeProvider(DateTimeOffset.UtcNow));

        Assert.False(registry.IsHeld("nope"));
    }
}
