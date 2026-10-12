using System.Globalization;
using System.Security.Claims;
using EggIdentity.Auth;
using EggIdentity.Contract;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.ApiKeys;
using EggIncognito.Services.DataApi;

namespace EggIncognito.Services.Auth;

public sealed class ApiFeatureGate(ICurrentUser currentUser, ApiFeatureGrantCache cache, IServiceProvider services) {
    public async Task<FeatureVerdict> CheckAsync(ClaimsPrincipal principal, string feature, CancellationToken ct) {
        bool admin = currentUser.Current.IsAtLeast(UserRole.Admin);
        int? keyId = int.TryParse(principal.FindFirst(ApiKeyGen.Claim)?.Value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int id) ? id : null;
        if (admin || keyId is null) return Evaluate(admin, keyId, null);

        string? state = await cache.GetAsync(keyId.Value, feature,
            () => services.GetService<ApiKeyFeatureStore>() is { } store
                ? store.StateAsync(keyId.Value, feature, ct)
                : Task.FromResult<string?>(null));
        return Evaluate(false, keyId, state);
    }

    public static FeatureVerdict Evaluate(bool admin, int? keyId, string? state) =>
        admin ? FeatureVerdict.Allow
        : keyId is null ? new FeatureVerdict(403, "api key required", null)
        : state == ApiKeyFeatureStates.Approved ? FeatureVerdict.Allow
        : new FeatureVerdict(403, "feature not granted", state ?? "none");
}
