namespace EggIncognito.Runner.State;

public sealed class VersionState(string path) {
    public string LastSeen() =>
        File.Exists(path) ? File.ReadAllText(path).Trim() : "";

    public void Save(string version) => File.WriteAllText(path, version);
}
