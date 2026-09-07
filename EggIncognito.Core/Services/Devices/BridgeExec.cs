namespace EggIncognito.Core.Services.Devices;

public sealed record BridgeExecSpec(
    string Exe,
    IReadOnlyList<string> Args,
    IReadOnlyList<string>? Outputs = null,
    int? TimeoutMs = null,
    bool BinaryStdout = false);

public sealed record BridgeExecOutput(string Name, string Base64);

public sealed record BridgeExecResult(
    int Exit,
    string? Stdout,
    string? StdoutBase64,
    string Stderr,
    IReadOnlyList<BridgeExecOutput> Outputs);

public static class BridgeExecParts {
    public const string Spec = "spec";
}
