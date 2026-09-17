namespace EggIncognito.Tests;

public static class TestFixtureFiles {
    public static bool TryRead(string name, out byte[] bytes) {
        string full = Path.Combine(TestPaths.WebProjectDir(), "captures", "fixtures", name);
        if (!File.Exists(full)) {
            bytes = [];
            return false;
        }

        bytes = File.ReadAllBytes(full);
        return true;
    }
}
