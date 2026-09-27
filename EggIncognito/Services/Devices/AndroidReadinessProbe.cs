using System.Text;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public sealed class AndroidReadinessProbe(
    IDeviceConnectionFactory connections,
    ProxyReachProbe proxyReach,
    CaptureCaSource captureCa) {
    private const string GmsPackage = "com.google.android.gms";
    private const string SystemCaCerts = "/system/etc/security/cacerts/";
    private const string SectionMarker = "egi-readiness:";
    private const string BootSection = "boot";
    private const string InstalledSection = "installed";
    private const string PlaySection = "play";
    private const string PidSection = "pid";
    private const string FocusSection = "focus";
    private const string GsfSection = "gsf";
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30);

    private static readonly string RootCommand = Compound((GsfSection, GsfIdentity.AndroidIdQuery));

    public async Task<DeviceReadiness> ProbeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android)) {
            var na = new ReadinessCheck(false, "android only");
            return new DeviceReadiness(na, na, na, na, na, na);
        }

        if (connections.For(target) is not { } conn) {
            var no = new ReadinessCheck(false, "no connection");
            return new DeviceReadiness(no, no, no, no, no, no);
        }

        var plain = Sections((await ShellAsync(conn, PlainCommand(target.Package), ct)).Stdout);
        if (Offline(plain) is { } offline)
            return new DeviceReadiness(offline, offline, offline, offline, offline, offline);

        var proxyTask = ProxyReachableAsync(target, ct);
        var root = await DeviceRoot.ProbeAsync(conn, ct);
        var rooted = await ShellAsync(conn, root.Wrap(RootCommand), ct);
        var rootSections = Sections(rooted.Stdout);
        var ca = await CaptureCaAsync(conn, root, ct);
        var proxy = await proxyTask;
        return new DeviceReadiness(
            Installed(plain),
            ca,
            GooglePlay(plain, rootSections, root),
            RootedCheck(root),
            Launched(plain),
            proxy);
    }

    private static string PlainCommand(string package) => Compound(
        (BootSection, "getprop sys.boot_completed"),
        (InstalledSection, $"pm path {package}"),
        (PlaySection, $"pm list packages {GmsPackage}"),
        (PidSection, $"pidof {package}"),
        (FocusSection, DeviceForeground.FocusCommand));

    private static string Compound(params (string Name, string Command)[] sections) =>
        string.Join("; ", sections.Select(s => $"echo {SectionMarker}{s.Name}; {s.Command}"));

    private static Dictionary<string, string> Sections(string stdout) {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        string? current = null;
        var body = new StringBuilder();
        foreach (string raw in stdout.Split('\n')) {
            string line = raw.TrimEnd('\r');
            string trimmed = line.Trim();
            if (trimmed.StartsWith(SectionMarker, StringComparison.Ordinal)) {
                if (current is not null) map[current] = body.ToString();
                current = trimmed[SectionMarker.Length..].Trim();
                body.Clear();
                continue;
            }

            if (current is not null) body.Append(line).Append('\n');
        }

        if (current is not null) map[current] = body.ToString();
        return map;
    }

    private static string Section(IReadOnlyDictionary<string, string> sections, string name) =>
        sections.GetValueOrDefault(name, "");

    private static async Task<ProcessResult> ShellAsync(IDeviceConnection conn, string command, CancellationToken ct) {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(QueryTimeout);
        var r = await conn.ShellAsync(command, bounded.Token);
        ct.ThrowIfCancellationRequested();
        return r;
    }

    private async Task<ReadinessCheck> ProxyReachableAsync(DeviceTarget target, CancellationToken ct) {
        var reach = await proxyReach.CheckAsync(target.Id, ct);
        return new ReadinessCheck(reach.Ok, reach.Note);
    }

    private static ReadinessCheck? Offline(IReadOnlyDictionary<string, string> sections) {
        string boot = Section(sections, BootSection).Trim();
        if (boot.Length == 0) return new ReadinessCheck(false, "device unreachable");
        return boot == "1" ? null : new ReadinessCheck(false, "device is booting");
    }

    private static ReadinessCheck Installed(IReadOnlyDictionary<string, string> sections) =>
        Section(sections, InstalledSection).Contains("package:", StringComparison.Ordinal)
            ? new ReadinessCheck(true)
            : new ReadinessCheck(false, "not installed");

    private static ReadinessCheck GooglePlay(
        IReadOnlyDictionary<string, string> plain, IReadOnlyDictionary<string, string> rooted, RootAccess root) {
        if (!Section(plain, PlaySection).Contains("package:", StringComparison.Ordinal))
            return new ReadinessCheck(false, "no Play services");

        if (GsfIdentity.Parse(Section(rooted, GsfSection)) is { } id) return new ReadinessCheck(true, $"gsf id {id}");
        return new ReadinessCheck(false, root.Ok
            ? "Play services installed but no android_id in gservices yet; check-in has not completed"
            : "no root, so the gservices android_id cannot be read from the shell uid");
    }

    private static ReadinessCheck RootedCheck(RootAccess root) =>
        root.Ok ? new ReadinessCheck(true) : new ReadinessCheck(false, root.Detail);

    private static ReadinessCheck Launched(IReadOnlyDictionary<string, string> sections) {
        if (Section(sections, PidSection).Trim().Length == 0) return new ReadinessCheck(false, "not running");

        var front = DeviceForeground.Parse(Section(sections, FocusSection));
        return front.Is(DeviceForeground.PlayStorePackage)
            ? new ReadinessCheck(false, DeviceForeground.PlayBlockNote)
            : new ReadinessCheck(true);
    }

    private async Task<ReadinessCheck> CaptureCaAsync(IDeviceConnection conn, RootAccess root, CancellationToken ct) {
        if (await captureCa.ResolveAsync(ct) is not { AndroidTrustFile: { } file })
            return new ReadinessCheck(false, "no capture CA minted");

        var r = await conn.ShellAsync(root.WrapMountMaster($"[ -s {SystemCaCerts}{file} ] && echo present"), ct);
        return r.Stdout.Contains("present", StringComparison.Ordinal)
            ? new ReadinessCheck(true)
            : new ReadinessCheck(false, "not in the trust store");
    }
}
