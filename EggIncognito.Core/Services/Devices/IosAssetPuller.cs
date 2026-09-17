namespace EggIncognito.Core.Services.Devices;

public sealed class IosAssetPuller(SshDeviceConnection conn) {
    private const string RemoteTar = "/tmp/egi-rpos.tar";

    public async Task<byte[]?> PullRposTarAsync(string bundleId, CancellationToken ct) {
        var make = await conn.ShellAsync(
            DeviceShell.LocateIosApp(bundleId) +
            "cd \"$app\" || exit 4; " +
            "find . \\( -iname '*.rpo' -o -iname '*.rpoz' \\) -print0 > /tmp/egi-rpos.list 2>/dev/null; " +
            "[ -s /tmp/egi-rpos.list ] || exit 5; " +
            $"tar --null -cf {RemoteTar} -T /tmp/egi-rpos.list 2>/dev/null || tar -cf {RemoteTar} $(find . \\( -iname '*.rpo' -o -iname '*.rpoz' \\)); " +
            $"rm -f /tmp/egi-rpos.list; [ -s {RemoteTar} ]", ct);
        if (make.ExitCode != 0) return null;
        try {
            return await conn.PullBytesAsync(RemoteTar, ct);
        } finally {
            await conn.ShellAsync($"rm -f {RemoteTar}", ct);
        }
    }

    public async Task<IReadOnlyList<string>> ListRposAsync(string bundleId, CancellationToken ct) {
        var r = await conn.ShellAsync(
            DeviceShell.LocateIosApp(bundleId) +
            "find \"$app\" \\( -iname '*.rpo' -o -iname '*.rpoz' \\) -exec basename {} \\; 2>/dev/null | sort -u", ct);
        return r.ExitCode != 0 ? [] : StemList(r.Stdout);
    }

    public Task<byte[]?> PullOneRpoAsync(string bundleId, string stem, CancellationToken ct) =>
        PullOneAsync(bundleId,
            $"\\( -name {DeviceShell.Quote(stem + ".rpo")} -o -name {DeviceShell.Quote(stem + ".rpoz")} \\)", ct);

    public async Task<IReadOnlyList<string>> ListTexturesAsync(string bundleId, CancellationToken ct) {
        var r = await conn.ShellAsync(
            DeviceShell.LocateIosApp(bundleId) +
            "find \"$app\" -iname '*.png' -exec basename {} \\; 2>/dev/null | sort -u", ct);
        return r.ExitCode != 0 ? [] : StemList(r.Stdout);
    }

    public Task<byte[]?> PullOneTextureAsync(string bundleId, string stem, CancellationToken ct) =>
        PullOneAsync(bundleId, $"-name {DeviceShell.Quote(stem + ".png")}", ct);

    public async Task<byte[]?> PullAppBinaryAsync(string bundleId, CancellationToken ct) {
        var find = await conn.ShellAsync(
            DeviceShell.LocateIosApp(bundleId) +
            "exe=\"$app/$(basename \"$app\" .app)\"; [ -f \"$exe\" ] && echo \"$exe\"", ct);
        return await PullFoundAsync(find, ct);
    }

    private async Task<byte[]?> PullOneAsync(string bundleId, string findPredicate, CancellationToken ct) {
        var find = await conn.ShellAsync(
            DeviceShell.LocateIosApp(bundleId) +
            $"find \"$app\" {findPredicate} 2>/dev/null | head -1", ct);
        return await PullFoundAsync(find, ct);
    }

    private async Task<byte[]?> PullFoundAsync(ProcessResult find, CancellationToken ct) {
        if (find.ExitCode != 0) return null;
        string? path = find.Stdout.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return string.IsNullOrEmpty(path) ? null : await conn.PullBytesAsync(path, ct);
    }

    private static IReadOnlyList<string> StemList(string output) => [
        .. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(StripExt).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal)
    ];

    private static string StripExt(string name) {
        int dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}
