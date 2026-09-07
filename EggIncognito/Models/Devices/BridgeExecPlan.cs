using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Models.Devices;

public sealed class BridgeExecPlan(BridgeExecSpec spec, DirectoryInfo dir) : IDisposable {
    public const int DefaultTimeoutMs = 10 * 60 * 1000;
    public const int MaxTimeoutMs = 60 * 60 * 1000;

    public BridgeExecSpec Spec => spec;
    public DirectoryInfo Dir => dir;
    public Dictionary<string, string> Inputs { get; } = [];
    public Dictionary<string, string> Outputs { get; } = [];
    public string[] Args { get; set; } = [];

    public int TimeoutMs => Math.Clamp(spec.TimeoutMs ?? DefaultTimeoutMs, 1, MaxTimeoutMs);

    public IReadOnlyList<BridgeExecOutput> CollectOutputs() {
        var collected = new List<BridgeExecOutput>();
        foreach ((string name, string path) in Outputs) {
            if (!File.Exists(path)) continue;
            collected.Add(new BridgeExecOutput(name, Convert.ToBase64String(File.ReadAllBytes(path))));
        }

        return collected;
    }

    public void Dispose() => TryPurge();

    private bool TryPurge() {
        try {
            dir.Delete(true);
            return true;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException) {
            return false;
        }
    }
}
