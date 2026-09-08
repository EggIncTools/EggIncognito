using System.Text;
using System.Text.RegularExpressions;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed partial class CreateIslandStep(
    IServiceScopeFactory scopeFactory,
    IDeviceConnectionFactory connections,
    IProcessRunner runner) : CookbookStep {
    private const string SecSetupWizard = "com.sec.android.app.SecSetupWizard";
    private const int MaxUsers = 8;
    private const string ModuleId = "egi_islands_maxusers";
    private const string ModuleDir = "/data/adb/modules/" + ModuleId;
    private const string PropRemote = "/data/local/tmp/egi-island-module.prop";
    private const string ScriptRemote = "/data/local/tmp/egi-island-postfsdata.sh";

    private static readonly byte[] ModuleProp = Encoding.ASCII.GetBytes(
        "id=" + ModuleId + "\n"
        + "name=Island max users\n"
        + "version=1\n"
        + "versionCode=1\n"
        + "author=egi\n"
        + "description=Persists fw.max_users across reboot for multi-user islands\n");

    private static readonly byte[] PostFsData = Encoding.ASCII.GetBytes(
        "#!/system/bin/sh\nresetprop fw.max_users " + MaxUsers + "\n");

    [GeneratedRegex(@"created user id (\d+)")]
    private static partial Regex CreatedUserRegex();

    public override string Id => DeviceCookbookIds.CreateIsland;
    public override string Title => "Create island";

    public override Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Task.FromResult(CookbookStepAvailability.No("islands are android-only"));
        if (connections.For(target) is null)
            return Task.FromResult(CookbookStepAvailability.No("no connection for this device"));

        return Task.FromResult(new CookbookStepAvailability(true, null, "Label"));
    }

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        void Add(string line) {
            lines.Add(line);
            context.Progress(line);
        }

        var target = context.Target;
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Skipped(lines, "islands are android-only");
        if (connections.For(target) is not { } conn)
            return Failed(lines, "no connection for this device");

        string label = string.IsNullOrWhiteSpace(context.Argument) ? "island" : context.Argument.Trim();

        var root = await DeviceRoot.EnsureAsync(conn, runner, target.Target, ct);
        if (!root.Ok)
            return Failed(lines, $"device is not rooted ({root.Detail}); lifting the user cap needs uid=0 (su)");
        Add($"root: {root.Detail}");

        var lift = await conn.ShellAsync(root.Wrap($"resetprop fw.max_users {MaxUsers}"), ct);
        Add(lift.ExitCode == 0
            ? $"fw.max_users set to {MaxUsers} for this boot"
            : $"resetprop fw.max_users failed (exit {lift.ExitCode}): {DeviceParsing.TrimNote(lift.Stderr + lift.Stdout)}");

        await PersistMaxUsersAsync(conn, root, Add, ct);

        Add($"creating user '{label}'");
        var create = await conn.ShellAsync($"pm create-user {DeviceShell.Quote(label)}", ct);
        var match = CreatedUserRegex().Match(create.Stdout + create.Stderr);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int userId)) {
            return Failed(lines,
                $"pm create-user did not report a new id: {DeviceParsing.TrimNote(create.Stdout + create.Stderr)}");
        }

        Add($"created user id {userId}");
        bool provisioned = await ProvisionAsync(conn, userId, Add, ct);
        await WriteRowAsync(target.Id, userId, label, provisioned, Add, ct);
        return Ok(lines, $"island {userId} '{label}' created");
    }

    private static async Task<bool> ProvisionAsync(
        IDeviceConnection conn, int userId, Action<string> add, CancellationToken ct) {
        string user = IslandScope.User(userId);
        var global = await conn.ShellAsync("settings put global device_provisioned 1", ct);
        var setup = await conn.ShellAsync($"settings put --user {user} secure user_setup_complete 1", ct);
        var wizard = await conn.ShellAsync($"pm disable-user --user {user} {SecSetupWizard}", ct);

        bool ok = global.ExitCode == 0 && setup.ExitCode == 0;
        add(ok
            ? $"provisioned user {user} (device_provisioned, user_setup_complete)"
            : "provisioning wrote partial settings; the island may show setup interstitials");
        if (wizard.ExitCode != 0)
            add($"SecSetupWizard disable: {DeviceParsing.TrimNote(wizard.Stdout + wizard.Stderr)}");
        return ok;
    }

    private async Task PersistMaxUsersAsync(
        IDeviceConnection conn, RootAccess root, Action<string> add, CancellationToken ct) {
        string? prop = await PushAsync(conn, ModuleProp, "-island-module.prop", PropRemote, ct);
        string? script = await PushAsync(conn, PostFsData, "-island-postfsdata.sh", ScriptRemote, ct);
        if (prop is null || script is null) {
            add("could not stage the persistence module; fw.max_users will reset on reboot");
            return;
        }

        string install =
            $"mkdir -p {ModuleDir} && cp {PropRemote} {ModuleDir}/module.prop && "
            + $"cp {ScriptRemote} {ModuleDir}/post-fs-data.sh && chmod 0755 {ModuleDir}/post-fs-data.sh && "
            + $"chmod 0644 {ModuleDir}/module.prop && rm -f {PropRemote} {ScriptRemote}";
        var wrote = await conn.ShellAsync(root.Wrap(install), ct);
        add(wrote.ExitCode == 0
            ? $"persistence module installed at {ModuleDir} (takes effect on next reboot)"
            : $"persistence module install failed (exit {wrote.ExitCode}): {DeviceParsing.TrimNote(wrote.Stderr + wrote.Stdout)}");
    }

    private static async Task<string?> PushAsync(
        IDeviceConnection conn, byte[] bytes, string localSuffix, string remote, CancellationToken ct) {
        string local = DeviceShell.NewTempPath(localSuffix);
        try {
            await File.WriteAllBytesAsync(local, bytes, ct);
            return await conn.PushFileAsync(local, remote, ct) ? remote : null;
        } finally {
            DeviceShell.TryDelete(local);
        }
    }

    private async Task WriteRowAsync(
        string deviceId, int userId, string label, bool provisioned, Action<string> add, CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(DeviceIslandStore)) is not DeviceIslandStore store) {
            add("no database configured, island not recorded");
            return;
        }

        await store.UpsertAsync(deviceId, userId, label, provisioned, null, ct);
        add($"island {userId} recorded");
    }
}
