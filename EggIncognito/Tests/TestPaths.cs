namespace EggIncognito.Tests;

internal static class TestPaths {
    public static string RepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"repo root not found: no *.slnx or *.sln in any parent of {AppContext.BaseDirectory}");
    }

    public static string WebProjectDir() => Path.Combine(RepoRoot(), "EggIncognito");

    public static string FixturesDir() => Path.Combine(WebProjectDir(), "Tests", "TestFixtures");
}
