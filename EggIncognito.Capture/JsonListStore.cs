using System.Text.Json;

namespace EggIncognito.Capture;

public abstract class JsonListStore<T>(string capturePath, string fileName) {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Lock _gate = new();

    private string FilePath => Path.Combine(capturePath, fileName);

    public IReadOnlyList<T> Load() {
        try {
            return !File.Exists(FilePath)
                ? []
                : (IReadOnlyList<T>)(JsonSerializer.Deserialize<List<T>>(File.ReadAllText(FilePath), Json) ?? []);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) {
            CaptureDiagnostics.Failed("store read", FilePath, ex);
            return [];
        }
    }

    protected void Mutate(Action<List<T>> mutate) {
        lock (_gate) {
            var rows = Load().ToList();
            mutate(rows);
            TryWrite(rows);
        }
    }

    protected void Replace(IEnumerable<T> rows) {
        lock (_gate) TryWrite([.. rows]);
    }

    private void TryWrite(List<T> rows) {
        try {
            Directory.CreateDirectory(capturePath);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(rows, Json));
            File.Move(tmp, FilePath, true);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) {
            CaptureDiagnostics.Failed("store write", FilePath, ex);
        }
    }
}
