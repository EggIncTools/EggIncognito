using EggIncognito.Models.ApiKeys;

namespace EggIncognito.Services.Auth;

public static class ApiFeatures {
    public const string ConsumeObservations = "consume-observations";

    public static readonly IReadOnlyList<ApiFeature> All = [
        new(ConsumeObservations, "Consume observations",
            "Artifact consume drop data: per-cell aggregates and every raw observation.")
    ];

    public static ApiFeature? Find(string key) => All.FirstOrDefault(f => f.Key == key);
}
