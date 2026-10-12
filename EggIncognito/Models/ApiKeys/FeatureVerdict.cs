namespace EggIncognito.Models.ApiKeys;

public sealed record FeatureVerdict(int Status, string? Error, string? State) {
    public static readonly FeatureVerdict Allow = new(200, null, null);
    public bool Allowed => Status == 200;
}
