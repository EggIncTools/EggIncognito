using System.Text;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public sealed class VirtualDeviceReadinessProbe(
    IDeviceConnectionFactory connections,
    VirtualDeviceConfig config,
    ProxyReachProbe proxyReach,
    CaptureCaSource captureCa) {
    private const string SystemCaCerts = "/system/etc/security/cacerts/";
    private const string SectionMarker = "egi-readiness:";
    private const string BootSection = "boot";
    private const string InstalledSection = "installed";
    private const string PlaySection = "play";
    private const string PidSection = "pid";
    private const string FocusSection = "focus";
    private const string SeedSection = "seed";
    private const string GsfSection = "gsf";
    private const string ModulesSection = "mods";
    private const string ChainSection = "chain";
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30);

    private static readonly string RootCommand = Compound(
        (GsfSection, GsfIdentity.AndroidIdQuery),
        (ModulesSection, MagiskModules.ScanCommand),
        (ChainSection, IntegrityChain.StateCommand));

    public async Task<DeviceReadiness> ProbeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android)) {
            var na = new ReadinessCheck(false, "android only");
            return new DeviceReadiness(na, na, na, na, na, na, na);
        }

        if (connections.For(target) is not { } conn) {
            var no = new ReadinessCheck(false, "no connection");
            return new DeviceReadiness(no, no, no, no, no, no, no);
        }

        var plain = Sections((await ShellAsync(conn, PlainCommand(target.Package), ct)).Stdout);
        if (Offline(plain) is { } offline)
            return new DeviceReadiness(offline, offline, offline, offline, offline, offline, offline);

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
            Integrity(rootSections, rooted.Stderr, plain),
            Launched(plain),
            proxy);
    }

    public async Task<(bool ModulesLive, IntegrityChainState? Chain)> ChainAsync(DeviceTarget target, CancellationToken ct) {
        if (connections.For(target) is not { } conn) return (false, null);
        var root = await DeviceRoot.ProbeAsync(conn, ct);
        var scan = await conn.ShellAsync(root.Wrap(MagiskModules.ScanCommand), ct);
        if (!MagiskModules.Ran(scan.Stdout)) return (false, null);
        var mods = MagiskModules.Live(MagiskModules.Parse(scan.Stdout));
        bool live = mods.Count >= config.IntegrityModules.Count && mods.TrueForAll(m => m.Ok);
        var chain = await conn.ShellAsync(root.Wrap(IntegrityChain.StateCommand), ct);
        return (live, IntegrityChain.Ran(chain.Stdout) ? IntegrityChain.Parse(chain.Stdout) : null);
    }

    private string PlainCommand(string package) => Compound(
        (BootSection, "getprop sys.boot_completed"),
        (InstalledSection, $"pm path {package}"),
        (PlaySection, $"pm list packages {config.GmsPackage}"),
        (PidSection, $"pidof {package}"),
        (FocusSection, DeviceForeground.FocusCommand),
        (SeedSection, IntegritySeed.ProbeCommand));

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
            return new ReadinessCheck(false, "no Play services, needs a gapps image");

        if (GsfIdentity.Parse(Section(rooted, GsfSection)) is { } id) return new ReadinessCheck(true, $"gsf id {id}");
        return new ReadinessCheck(false, root.Ok
            ? "Play services installed but no android_id in gservices yet; check-in has not completed"
            : "no root, so the gservices android_id cannot be read from the shell uid");
    }

    private static ReadinessCheck RootedCheck(RootAccess root) =>
        root.Ok ? new ReadinessCheck(true) : new ReadinessCheck(false, root.Detail);

    private ReadinessCheck Integrity(
        IReadOnlyDictionary<string, string> rooted, string stderr, IReadOnlyDictionary<string, string> plain) {
        string scan = Section(rooted, ModulesSection);
        if (!MagiskModules.Ran(scan))
            return new ReadinessCheck(false, $"module scan did not run: {DeviceParsing.TrimNote(stderr + scan)}");

        var mods = MagiskModules.Live(MagiskModules.Parse(scan));
        if (mods.Count == 0) {
            var seed = IntegritySeed.Parse(Section(plain, SeedSection));
            return seed.SeededImage && seed.State != IntegritySeed.StateDone
                ? new ReadinessCheck(false, "first-boot seed still installing the chain")
                : new ReadinessCheck(false, "no module in /data/adb/modules");
        }

        string listing = MagiskModules.Describe(mods);
        if (mods.Exists(m => !m.Ok)) return new ReadinessCheck(false, $"disabled module: {listing}");

        int want = config.IntegrityModules.Count;
        if (mods.Count < want)
            return new ReadinessCheck(false, $"{mods.Count} of {want} chain modules present: {listing}");

        string chainOut = Section(rooted, ChainSection);
        if (!IntegrityChain.Ran(chainOut)) return new ReadinessCheck(false, $"{listing}; chain state probe did not run");
        var chain = IntegrityChain.Parse(chainOut);
        return chain.Activated
            ? new ReadinessCheck(true, $"{listing}; {chain.Describe()}")
            : new ReadinessCheck(false, $"installed but not activated ({chain.Describe()}); run activate-integrity");
    }

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
