namespace EggIncognito.Tests;

internal static class SheetFetch {
    public const string SharedPath = "/_content/EggIdentity.Styles/shared.css";
    public const string AppPath = "/app.css";

    public static async Task<string> AllAsync(HttpClient c) {
        string shared = await c.GetStringAsync(SharedPath);
        string app = await c.GetStringAsync(AppPath);
        string scoped = await c.GetStringAsync("/EggIncognito.styles.css");
        return shared + "\n" + app + "\n" + scoped;
    }
}
