namespace EggIncognito.Services.Feed;

public static class FeedPages {
    public const string RootKey = "Feed:PageBaseUrl";
    public const string DefaultPageBaseUrl = "https://eggincognito.egginc.tools";

    public static string Root(IConfiguration cfg) {
        string? configured = cfg[RootKey];
        return string.IsNullOrEmpty(configured) ? DefaultPageBaseUrl : configured.TrimEnd('/');
    }

    public static string Url(string root, string path) =>
        $"{root.TrimEnd('/')}/{path.TrimStart('/')}";
}
