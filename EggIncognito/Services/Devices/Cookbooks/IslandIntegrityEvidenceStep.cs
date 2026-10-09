using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class IslandIntegrityEvidenceStep(IDeviceConnectionFactory connections) : CookbookStep {
    private const string PifDir = "/data/adb/modules/playintegrityfix";

    private const string DenylistCommand =
        "magisk --denylist status 2>&1; magisk --denylist ls 2>&1 | grep -i -E 'google|vending|auxbrain'";

    private const string PifCommand =
        "for f in " + PifDir + "/custom.pif.json " + PifDir + "/pif.json /data/adb/pif.json; do "
        + "[ -f \"$f\" ] && echo \"file=$f\" && grep -E '\"(FINGERPRINT|SECURITY_PATCH|MODEL|spoof[A-Za-z]*)\"' \"$f\"; done";

    private const string ProcessCommand = "ps -A -o USER,PID,NAME 2>/dev/null | grep -E 'gms.unstable|vending'";

    private const string LogCommand =
        "logcat -d 2>/dev/null | grep -i -E 'PIF|TrickyStore|TEESimulator|DroidGuard|integrity|attest|certif|KeyAttestation' "
        + "| grep -v -i 'egginc' | tail -n 60";

    public override string Id => "island-integrity-evidence";
    public override string Title => "Integrity evidence";

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        Task Add(string line) {
            lines.Add(line);
            return context.Progress(line);
        }

        if (connections.For(context.Target) is not { } conn)
            return Failed(lines, "no connection for this device");

        var root = await DeviceRoot.ProbeAsync(conn, ct);
        if (!root.Ok) return Failed(lines, $"device is not rooted ({root.Detail}); evidence needs su");

        await SectionAsync(conn, root, "denylist", DenylistCommand, Add, ct);
        await SectionAsync(conn, root, "pif config", PifCommand, Add, ct);
        await SectionAsync(conn, root, "integrity processes", ProcessCommand, Add, ct);
        await SectionAsync(conn, root, "logcat since launch", LogCommand, Add, ct);
        return Ok(lines, "evidence captured");
    }

    private static async Task SectionAsync(
        IDeviceConnection conn, RootAccess root, string title, string command, Func<string, Task> add,
        CancellationToken ct) {
        var r = await conn.ShellAsync(root.Wrap(command), ct);
        string[] output = [.. r.Stdout.Split('\n').Select(l => l.TrimEnd('\r').TrimEnd()).Where(l => l.Length > 0)];
        await add(output.Length == 0 ? $"{title}: nothing" : $"{title}:");
        foreach (string line in output) await add("  " + line);
    }
}
