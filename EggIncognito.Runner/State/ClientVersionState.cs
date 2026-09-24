using System.Globalization;

namespace EggIncognito.Runner.State;

public sealed class ClientVersionState(string path, int? seed) {
    public int? Last() {
        if (File.Exists(path) &&
            int.TryParse(File.ReadAllText(path).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return v;
        return seed;
    }

    public void Save(int value) => File.WriteAllText(path, value.ToString(CultureInfo.InvariantCulture));
}
