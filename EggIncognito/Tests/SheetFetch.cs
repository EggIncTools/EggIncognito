namespace EggIncognito.Tests;

internal static class SheetFetch {
    public static async Task<string> AllAsync(HttpClient c) {
        string global = await c.GetStringAsync("/styles.css");
        string scoped = await c.GetStringAsync("/EggIncognito.styles.css");
        return global + "\n" + scoped;
    }
}
