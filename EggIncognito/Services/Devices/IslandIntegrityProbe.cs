using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public static class IslandIntegrityProbe {
    public const string GmsPackage = "com.google.android.gms";
    public const string GsfPackage = "com.google.android.gsf";
    public const string TrickyStoreDir = "/data/adb/tricky_store";
    public const string TargetFile = TrickyStoreDir + "/target.txt";
    public const string KeyboxFile = TrickyStoreDir + "/keybox.xml";

    public static readonly string[] PlayPackages = [GmsPackage, DeviceForeground.PlayStorePackage, GsfPackage];

    private const string RootFacts =
        "echo \"vbs=$(getprop ro.boot.verifiedbootstate)\"; echo \"fp=$(getprop ro.build.fingerprint)\"; "
        + "for d in /data/adb/modules/*/; do [ -d \"$d\" ] || continue; n=$(basename \"$d\"); "
        + "if [ -f \"$d/disable\" ] || [ -f \"$d/remove\" ]; then echo \"mod=$n off\"; else echo \"mod=$n on\"; fi; done; "
        + "[ -d " + TrickyStoreDir + " ] && echo ts=1; "
        + "[ -f " + KeyboxFile + " ] && echo \"kb=$(stat -c %y " + KeyboxFile + ")\"; "
        + "[ -f " + TargetFile + " ] && sed 's/^/tgt=/' " + TargetFile;

    public static async Task<IslandIntegrityReport> ReadAsync(
        IDeviceConnection conn, RootAccess root, int androidUserId, CancellationToken ct) {
        var facts = await conn.ShellAsync(root.Wrap(RootFacts), ct);
        var packages = await conn.ShellAsync(PackageStateCommand(androidUserId), ct);

        string? vbs = null, fp = null, kb = null;
        bool ts = false;
        var modules = new Dictionary<string, bool>(StringComparer.Ordinal);
        var targets = new List<string>();
        foreach (string raw in facts.Stdout.Split('\n')) {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("vbs=", StringComparison.Ordinal)) vbs = NullIfEmpty(line[4..]);
            else if (line.StartsWith("fp=", StringComparison.Ordinal)) fp = NullIfEmpty(line[3..]);
            else if (line.StartsWith("kb=", StringComparison.Ordinal)) kb = NullIfEmpty(line[3..]);
            else if (line == "ts=1") ts = true;
            else if (line.StartsWith("tgt=", StringComparison.Ordinal) && line[4..].Trim() is { Length: > 0 } t) targets.Add(t);
            else if (line.StartsWith("mod=", StringComparison.Ordinal) && line[4..].Split(' ') is [var name, var state])
                modules[name] = state == "on";
        }

        var island = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in packages.Stdout.Split('\n')) {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("pkg=", StringComparison.Ordinal) && line[4..].Split(' ') is [var p, var s]) island[p] = s;
        }

        string? islandGsf = await ReadGsfAsync(conn, root, androidUserId, ct);
        string? ownerGsf = await GsfIdentity.ReadAsync(conn, root, ct);
        return new IslandIntegrityReport(vbs, fp, modules, ts, kb, targets, island, islandGsf, ownerGsf);
    }

    public static async Task<string?> ReadGsfAsync(
        IDeviceConnection conn, RootAccess root, int androidUserId, CancellationToken ct) {
        string user = IslandScope.User(androidUserId);
        var r = await conn.ShellAsync(root.Wrap(
            $"content query --user {user} --uri content://com.google.android.gsf.gservices "
            + "--projection value --where \"name='android_id'\" 2>/dev/null; "
            + $"cat /data/user/{user}/{GmsPackage}/shared_prefs/Checkin.xml 2>/dev/null"), ct);
        return GsfIdentity.Parse(r.Stdout);
    }

    public static IReadOnlyList<string> Findings(IslandIntegrityReport r, string package, int androidUserId) {
        string user = IslandScope.User(androidUserId);
        var findings = new List<string>();
        if (!string.Equals(r.VerifiedBootState, "green", StringComparison.OrdinalIgnoreCase))
            findings.Add($"verified boot state is {r.VerifiedBootState ?? "unknown"}; strong integrity needs a TrickyStore keybox to spoof attestation");
        if (!r.TrickyStore) findings.Add($"TrickyStore is not installed ({TrickyStoreDir} missing)");
        else if (r.KeyboxModified is null) findings.Add($"TrickyStore has no keybox ({KeyboxFile} missing)");
        if (!r.HasModule("playintegrity", "pif", "integrity_box", "integritybox"))
            findings.Add("no enabled Play Integrity Fix / Integrity Box module; device integrity verdict will fail");
        if (r.TrickyStore) {
            foreach (string p in TargetsFor(package).Where(p => !r.IsTargeted(p)))
                findings.Add($"{p} is not in TrickyStore target.txt");
        }

        foreach (string p in PlayPackages) {
            string state = r.IslandPackages.GetValueOrDefault(p, "missing");
            if (state != "enabled") findings.Add($"{p} is {state} in user {user}");
        }

        if (r.IslandGsfId is null) findings.Add($"GMS has not checked in for user {user} (no gsf android_id)");
        if (r.OwnerGsfId is not null && r.OwnerGsfId == r.IslandGsfId)
            findings.Add($"user {user} shares the owner gsf id {r.OwnerGsfId}");
        return findings;
    }

    public static IEnumerable<string> Describe(IslandIntegrityReport r, int androidUserId) {
        string user = IslandScope.User(androidUserId);
        yield return $"boot state {r.VerifiedBootState ?? "unknown"}; fingerprint {r.Fingerprint ?? "unknown"}";
        string modules = r.Modules.Count == 0
            ? "none"
            : string.Join(", ", r.Modules.Select(m => m.Value ? m.Key : $"{m.Key} (off)"));
        yield return $"modules: {modules}";
        yield return r.TrickyStore
            ? $"trickystore keybox {(r.KeyboxModified is { } kb ? $"modified {kb}" : "missing")}; targets {r.Targets.Count}"
            : "trickystore absent";
        yield return $"user {user} play packages: "
            + string.Join(", ", PlayPackages.Select(p => $"{p} {r.IslandPackages.GetValueOrDefault(p, "missing")}"));
        yield return $"gsf id: owner {r.OwnerGsfId ?? "none"}, user {user} {r.IslandGsfId ?? "none"}";
    }

    public static IEnumerable<string> TargetsFor(string package) =>
        new[] { GmsPackage, DeviceForeground.PlayStorePackage, package }.Distinct(StringComparer.Ordinal);

    private static string PackageStateCommand(int androidUserId) {
        string user = IslandScope.User(androidUserId);
        return string.Join("; ", PlayPackages.Select(p =>
            $"if pm list packages --user {user} {p} 2>/dev/null | grep -qx 'package:{p}'; then "
            + $"if pm list packages -d --user {user} {p} 2>/dev/null | grep -qx 'package:{p}'; "
            + $"then echo 'pkg={p} disabled'; else echo 'pkg={p} enabled'; fi; else echo 'pkg={p} missing'; fi"));
    }

    private static string? NullIfEmpty(string s) => s.Trim() is { Length: > 0 } t ? t : null;
}
