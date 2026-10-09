using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class IslandIntegrityDiagnoseStep(IDeviceConnectionFactory connections) : CookbookStep {
    public override string Id => "island-integrity-diagnose";
    public override string Title => "Diagnose integrity";

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        Task Add(string line) {
            lines.Add(line);
            return context.Progress(line);
        }

        var target = context.Target;
        if (context.AndroidUserId is not { } androidUserId)
            return Failed(lines, "no island selected; this step needs a target island user id");
        if (connections.For(target) is not { } conn)
            return Failed(lines, "no connection for this device");

        var root = await DeviceRoot.ProbeAsync(conn, ct);
        if (!root.Ok) return Failed(lines, $"device is not rooted ({root.Detail}); integrity checks need su");

        var report = await IslandIntegrityProbe.ReadAsync(conn, root, androidUserId, ct);
        foreach (string line in IslandIntegrityProbe.Describe(report, androidUserId)) await Add(line);

        var findings = IslandIntegrityProbe.Findings(report, target.Package, androidUserId);
        if (findings.Count == 0) return Ok(lines, "no integrity problems found");

        foreach (string finding in findings) await Add($"finding: {finding}");
        return Ok(lines, $"{findings.Count} finding(s)");
    }
}
