using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class CloneIslandTrustStep(
    IServiceScopeFactory scopeFactory,
    IDeviceConnectionFactory connections,
    IProcessRunner runner) : CookbookStep {
    private const string Sqlite = "LD_LIBRARY_PATH=/data/adb/sqlite /data/adb/sqlite/sqlite3";
    private const string KeystoreDb = "/data/misc/keystore/persistent.sqlite";
    private const string SdtRelative = "files/finsky/shared/secure_data_transfer_valuestore.pb";
    private const int UserIdTag = 805306869;

    public override string Id => DeviceCookbookIds.CloneIslandTrust;
    public override string Title => "Clone island trust";

    public override Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Task.FromResult(CookbookStepAvailability.No("islands are android-only"));
        if (connections.For(target) is null)
            return Task.FromResult(CookbookStepAvailability.No("no connection for this device"));

        return Task.FromResult(CookbookStepAvailability.Ready);
    }

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        Task Add(string line) {
            lines.Add(line);
            return context.Progress(line);
        }

        var target = context.Target;
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Skipped(lines, "islands are android-only");
        if (context.AndroidUserId is not { } source)
            return Failed(lines, "no source island selected; pick the island whose Play trust should be cloned");
        if (connections.For(target) is not { } conn)
            return Failed(lines, "no connection for this device");

        int? newest = await NewestIslandStep.NewestAsync(scopeFactory, target.Id, ct);
        if (newest is not { } dest)
            return Failed(lines, "no recorded island to clone into");
        if (dest == source)
            return Skipped(lines, $"island {dest} is the newest island and the source; nothing to clone");

        var root = await DeviceRoot.EnsureAsync(conn, runner, target.Target, ct);
        if (!root.Ok) return Failed(lines, $"device is not rooted ({root.Detail})");

        var tool = await conn.ShellAsync(root.WrapMountMaster($"{Sqlite} -version"), ct);
        if (tool.ExitCode != 0)
            return Failed(lines, $"sqlite3 is not available under /data/adb/sqlite: {DeviceParsing.TrimNote(tool.Stderr + tool.Stdout)}");

        string src = IslandScope.User(source);
        string dst = IslandScope.User(dest);
        string package = target.Package;

        await conn.ShellAsync(root.WrapMountMaster($"am start-user {src} >/dev/null 2>&1; am start-user {dst} >/dev/null 2>&1"), ct);

        var ids = await conn.ShellAsync(root.WrapMountMaster(
            $"stat -c %u /data/user/{src}/{DeviceForeground.PlayStorePackage} /data/user/{dst}/{DeviceForeground.PlayStorePackage}"), ct);
        var uids = ids.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (ids.ExitCode != 0 || uids.Length != 2
            || !long.TryParse(uids[0], out long srcUid) || !long.TryParse(uids[1], out long dstUid))
            return Failed(lines, $"could not resolve Play uids for users {src}/{dst}: {DeviceParsing.TrimNote(ids.Stdout + ids.Stderr)}");

        await Add($"cloning Play trust for {package}: user {src} (uid {srcUid}) -> user {dst} (uid {dstUid})");

        string aliasLike = $"anti_tamper_protection_key_{package}_%";
        var find = await conn.ShellAsync(root.WrapMountMaster(
            $"{Sqlite} {KeystoreDb} \"SELECT id FROM keyentry WHERE namespace={srcUid} AND alias LIKE '{aliasLike}' AND state=1 ORDER BY id LIMIT 1\""), ct);
        if (find.ExitCode != 0 || !long.TryParse(find.Stdout.Trim(), out long srcKey))
            return Failed(lines, $"user {src} holds no anti-tamper key for {package}; launch the app there first: {DeviceParsing.TrimNote(find.Stdout + find.Stderr)}");

        await Add($"source keystore row {srcKey}");

        await conn.ShellAsync(root.WrapMountMaster(
            $"am force-stop --user {src} {DeviceForeground.PlayStorePackage}; am force-stop --user {dst} {DeviceForeground.PlayStorePackage}; am force-stop --user {dst} {package}"), ct);

        var copy = await conn.ShellAsync(root.WrapMountMaster(
            $"cp -a /data/user/{src}/{DeviceForeground.PlayStorePackage}/{SdtRelative} /data/user/{dst}/{DeviceForeground.PlayStorePackage}/{SdtRelative}"
            + $" && chown {dstUid}:{dstUid} /data/user/{dst}/{DeviceForeground.PlayStorePackage}/{SdtRelative}"
            + $" && restorecon /data/user/{dst}/{DeviceForeground.PlayStorePackage}/{SdtRelative}"), ct);
        if (copy.ExitCode != 0)
            return Failed(lines, $"copying the secure data transfer store failed: {DeviceParsing.TrimNote(copy.Stderr + copy.Stdout)}");
        await Add("copied Play's secure data transfer store");

        var existing = await conn.ShellAsync(root.WrapMountMaster(
            $"{Sqlite} {KeystoreDb} \"SELECT count(*) FROM keyentry WHERE namespace={dstUid} AND alias LIKE '{aliasLike}'\""), ct);
        if (existing.Stdout.Trim() != "0") {
            await Add($"user {dst} already holds an anti-tamper keystore row; leaving keystore untouched");
            return await RelaunchAsync(conn, root, dst, lines, Add, ct);
        }

        var next = await conn.ShellAsync(root.WrapMountMaster(
            $"{Sqlite} {KeystoreDb} \"SELECT min(id)-1 FROM keyentry\""), ct);
        if (next.ExitCode != 0 || !long.TryParse(next.Stdout.Trim(), out long newKeyId))
            return Failed(lines, $"could not allocate a keyentry id: {DeviceParsing.TrimNote(next.Stderr + next.Stdout)}");

        await conn.ShellAsync(root.WrapMountMaster("stop keystore2"), ct);
        try {
            string sql = string.Join(" ", [
                "BEGIN;",
                $"INSERT INTO keyentry(id,key_type,domain,namespace,alias,state,km_uuid) SELECT {newKeyId},key_type,domain,{dstUid},alias,state,km_uuid FROM keyentry WHERE id={srcKey};",
                $"INSERT INTO blobentry(subcomponent_type,keyentryid,blob) SELECT subcomponent_type,{newKeyId},blob FROM blobentry WHERE keyentryid={srcKey};",
                $"INSERT INTO blobmetadata(blobentryid,tag,data) SELECT (SELECT id FROM blobentry WHERE keyentryid={newKeyId}),tag,data FROM blobmetadata WHERE blobentryid=(SELECT id FROM blobentry WHERE keyentryid={srcKey} LIMIT 1);",
                $"INSERT INTO keyparameter(keyentryid,tag,data,security_level) SELECT {newKeyId},tag,CASE WHEN tag={UserIdTag} THEN {dst} ELSE data END,security_level FROM keyparameter WHERE keyentryid={srcKey};",
                $"INSERT INTO keymetadata(keyentryid,tag,data) SELECT {newKeyId},tag,data FROM keymetadata WHERE keyentryid={srcKey};",
                "COMMIT;",
                $"SELECT id FROM keyentry WHERE namespace={dstUid} AND alias LIKE '{aliasLike}';"
            ]);
            var clone = await conn.ShellAsync(root.WrapMountMaster($"{Sqlite} {KeystoreDb} \"{sql}\""), ct);
            if (clone.ExitCode != 0 || !long.TryParse(clone.Stdout.Trim(), out long newKey))
                return Failed(lines, $"keystore clone failed: {DeviceParsing.TrimNote(clone.Stderr + clone.Stdout)}");
            await Add($"cloned keystore row {srcKey} -> {newKey} under uid {dstUid}");
        } finally {
            await conn.ShellAsync(root.WrapMountMaster("start keystore2"), ct);
        }

        return await RelaunchAsync(conn, root, dst, lines, Add, ct);
    }

    private async Task<CookbookStepResult> RelaunchAsync(
        IDeviceConnection conn, RootAccess root, string dst, List<string> lines, Func<string, Task> add, CancellationToken ct) {
        var ks = await conn.ShellAsync(root.WrapMountMaster("pidof keystore2"), ct);
        if (ks.Stdout.Trim().Length == 0)
            return Failed(lines, "keystore2 did not come back after the clone");
        await add($"keystore2 running (pid {ks.Stdout.Trim()})");
        return Ok(lines, $"island {dst} carries the cloned Play trust");
    }
}
