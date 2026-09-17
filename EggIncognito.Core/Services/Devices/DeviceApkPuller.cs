namespace EggIncognito.Core.Services.Devices;

public sealed class DeviceApkPuller(IProcessRunner runner) {
    private const string SplitBase = "base";
    private const string SplitArm64 = "arm64";

    public Task<byte[]?> PullArmSplitAsync(string serial, string package, CancellationToken ct) =>
        PullSplitAsync(serial, package, DeviceParsing.SelectArmSplit, ct);

    public Task<byte[]?> PullBaseSplitAsync(string serial, string package, CancellationToken ct) =>
        PullSplitAsync(serial, package, DeviceParsing.SelectBaseSplit, ct);

    public async Task<IReadOnlyList<PulledSplit>> PullAllSplitsAsync(
        string serial, string package, CancellationToken ct) {
        var conn = new AdbDeviceConnection(runner, serial);
        var pm = await conn.ShellAsync($"pm path {package}", ct);
        if (pm.ExitCode != 0) return [];

        var wanted = new List<(string Name, string Path)>();
        string? armPath = DeviceParsing.SelectArmSplit(pm.Stdout);
        if (DeviceParsing.SelectBaseSplit(pm.Stdout) is { } basePath)
            wanted.Add((SplitBase, basePath));
        if (armPath is not null)
            wanted.Add((SplitArm64, armPath));
        foreach (string cfg in DeviceParsing.SelectConfigSplits(pm.Stdout)) {
            if (cfg == armPath) continue;
            wanted.Add((DeviceParsing.SplitNameFromPath(cfg), cfg));
        }

        var pulled = new List<PulledSplit>();
        foreach ((string name, string path) in wanted) {
            byte[]? bytes = await conn.PullBytesAsync(path, ct);
            if (bytes is not null) pulled.Add(new PulledSplit(name, bytes));
        }

        return pulled;
    }

    private async Task<byte[]?> PullSplitAsync(
        string serial, string package, Func<string, string?> select, CancellationToken ct) {
        var conn = new AdbDeviceConnection(runner, serial);
        var pm = await conn.ShellAsync($"pm path {package}", ct);
        if (pm.ExitCode != 0) return null;
        string? path = select(pm.Stdout);
        return path is null ? null : await conn.PullBytesAsync(path, ct);
    }
}

public sealed record PulledSplit(string Split, byte[] Bytes);
