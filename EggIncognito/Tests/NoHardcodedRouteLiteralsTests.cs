namespace EggIncognito.Tests;

public class NoHardcodedRouteLiteralsTests {
    private static readonly string[] Routes =
        ["ei/get_periodicals", "ei_afx/config", "ei_ctx/get_season_infos_v2", "ei/get_config"];

    private static readonly string[] Allowed = ["DataCatalog.cs", "FeedEventKinds.cs"];

    [Fact]
    public void PeriodicalRouteLiterals_OnlyInCatalog() {
        string webDir = TestPaths.WebProjectDir();
        Assert.True(Directory.Exists(webDir), "web project dir not found");

        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(webDir, "*.cs", SearchOption.AllDirectories)) {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}")) continue;
            if (Allowed.Any(a => file.EndsWith(a, StringComparison.Ordinal))) continue;

            string text = File.ReadAllText(file);
            foreach (string route in Routes) {
                if (text.Contains($"\"{route}\"", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)} -> {route}");
            }
        }

        Assert.True(offenders.Count == 0,
            "periodical route literals belong in DataCatalog only, found: " + string.Join("; ", offenders));
    }
}
