using EggIncognito.Services.Auth;

namespace EggIncognito.Tests.Auth;

public sealed class ApiFeatureGrantCacheTests {
    [Fact]
    public async Task SecondRead_IsCached() {
        var cache = new ApiFeatureGrantCache(TimeProvider.System);
        int loads = 0;

        Task<string?> Load() {
            loads++;
            return Task.FromResult<string?>("approved");
        }

        await cache.GetAsync(1, "f", Load);
        await cache.GetAsync(1, "f", Load);
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task Invalidate_ForcesReload() {
        var cache = new ApiFeatureGrantCache(TimeProvider.System);
        string? state = "approved";
        Task<string?> Load() => Task.FromResult<string?>(state);

        Assert.Equal("approved", await cache.GetAsync(1, "f", Load));
        state = "revoked";
        cache.Invalidate(1);
        Assert.Equal("revoked", await cache.GetAsync(1, "f", Load));
    }
}
