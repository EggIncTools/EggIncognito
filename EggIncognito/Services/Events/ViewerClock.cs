using EggIdentity.UI;

namespace EggIncognito.Services.Events;

public sealed class ViewerClock(BrowserTimeZone browser) {
    private Task? _ensure;
    private string? _profileId;

    public TimeZoneInfo Zone => browser.Zone;

    public event Action? Changed {
        add => browser.Changed += value;
        remove => browser.Changed -= value;
    }

    public Task EnsureAsync(IServiceProvider services, ICurrentUser user, CancellationToken ct) =>
        _ensure ??= ResolveAsync(services, user, ct);

    public Task SyncAsync() => browser.SyncAsync(_profileId);

    private async Task ResolveAsync(IServiceProvider services, ICurrentUser user, CancellationToken ct) {
        _profileId = await ViewerZone.ProfileIdAsync(services, user, ct);
        browser.Apply(_profileId);
    }

    public static ViewerClock? Of(IServiceProvider services) => services.GetService(typeof(ViewerClock)) as ViewerClock;

    public static TimeZoneInfo ZoneOf(IServiceProvider services) => Of(services)?.Zone ?? ViewerZone.Fallback;

    public static DateTimeOffset At(IServiceProvider services, DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, ZoneOf(services));
}
