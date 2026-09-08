using System.Globalization;

namespace EggIncognito.Core.Services.Devices;

public static class IslandScope {
    public static string User(int userId) => userId.ToString(CultureInfo.InvariantCulture);

    public static string UserFlag(int? userId) =>
        userId is { } id ? $" --user {User(id)}" : "";

    public const int Owner = 0;

    public static async Task<DeviceResult> SwitchAsync(IDeviceConnection conn, int userId, CancellationToken ct) {
        string user = User(userId);
        if (userId != Owner) {
            var start = await conn.ShellAsync($"am start-user {user}", ct);
            if (start.ExitCode != 0 || start.Stdout.Contains("Error", StringComparison.Ordinal))
                return DeviceResult.Error($"am start-user {user}: {DeviceParsing.TrimNote(start.Stdout + start.Stderr)}");
        }

        var sw = await conn.ShellAsync($"am switch-user {user}", ct);
        if (sw.ExitCode != 0 || sw.Stdout.Contains("Error", StringComparison.Ordinal))
            return DeviceResult.Error($"am switch-user {user}: {DeviceParsing.TrimNote(sw.Stdout + sw.Stderr)}");

        for (int i = 0; i < 10; i++) {
            var cur = await conn.ShellAsync("am get-current-user", ct);
            if (cur.Stdout.Trim() == user) return DeviceResult.Success($"foreground user is {user}");
            await Task.Delay(500, ct);
        }

        var last = await conn.ShellAsync("am get-current-user", ct);
        return DeviceResult.Error($"switch to user {user} did not take; foreground user is {last.Stdout.Trim()}");
    }
}
