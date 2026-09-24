namespace EggIncognito.Services.Events;

public sealed class ViewerClock {
    private bool _resolved;

    public TimeZoneInfo Zone { get; private set; } = ViewerZone.Fallback;

    public async Task EnsureAsync(IServiceProvider services, ICurrentUser user, CancellationToken ct) {
        if (_resolved) return;
        _resolved = true;
        if (ViewerZone.Parse(await ViewerZone.ProfileIdAsync(services, user, ct)) is { } zone) Zone = zone;
    }

    public static TimeZoneInfo ZoneOf(IServiceProvider services) =>
        (services.GetService(typeof(ViewerClock)) as ViewerClock)?.Zone ?? ViewerZone.Fallback;

    public static DateTimeOffset At(IServiceProvider services, DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, ZoneOf(services));
}
