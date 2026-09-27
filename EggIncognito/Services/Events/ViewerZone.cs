using EggIdentity.Client;

namespace EggIncognito.Services.Events;

public static class ViewerZone {
    public static TimeZoneInfo Fallback => TimeZoneInfo.Local;

    public static async Task<string?> ProfileIdAsync(
        IServiceProvider services, ICurrentUser user, CancellationToken ct) {
        if (!user.IsAuthenticated) return null;
        if (services.GetService(typeof(AuthState)) is not AuthState auth) return null;
        if (services.GetService(typeof(IHttpContextAccessor)) is not IHttpContextAccessor accessor) return null;
        var token = accessor.HttpContext?.Request.Cookies[auth.SessionCookieName] ?? "";
        if (token.Length == 0) return null;
        if (services.GetService(typeof(IdentityApiClient)) is not IdentityApiClient identity) return null;
        try {
            var profile = await identity.GetProfileAsync(token, ct);
            return profile?.Timezone is { Length: > 0 } id ? id : null;
        } catch (HttpRequestException) {
            return null;
        }
    }

    public static TimeZoneInfo? Parse(string? id) {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        } catch (TimeZoneNotFoundException) {
            return null;
        } catch (InvalidTimeZoneException) {
            return null;
        }
    }
}
