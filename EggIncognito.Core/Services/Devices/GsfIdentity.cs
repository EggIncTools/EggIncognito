using System.Text.RegularExpressions;

namespace EggIncognito.Core.Services.Devices;

public static partial class GsfIdentity {
    public const string CheckinPrefs = "/data/data/com.google.android.gms/shared_prefs/Checkin.xml";

    public const string AndroidIdQuery =
        "content query --uri content://com.google.android.gsf.gservices "
        + "--projection value --where \"name='android_id'\" 2>/dev/null; "
        + "cat " + CheckinPrefs + " 2>&1";

    public const string CheckinCommand =
        "am broadcast -a android.server.checkin.CHECKIN -n com.google.android.gms/.checkin.CheckinService >/dev/null 2>&1; "
        + "am broadcast -a android.server.checkin.CHECKIN >/dev/null 2>&1; echo kicked=1";

    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(20);

    public static async Task<string?> ReadAsync(IDeviceConnection conn, RootAccess root, CancellationToken ct) {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(QueryTimeout);
        var r = await conn.ShellAsync(root.Wrap(AndroidIdQuery), bounded.Token);
        ct.ThrowIfCancellationRequested();
        return Parse(r.Stdout);
    }

    public static async Task<string?> WaitAsync(
        IDeviceConnection conn, RootAccess root, TimeSpan timeout, TimeSpan interval, Action<string>? progress,
        CancellationToken ct) {
        var started = DateTimeOffset.UtcNow;
        var deadline = started + timeout;
        while (true) {
            if (await ReadAsync(conn, root, ct) is { } id) return id;
            if (DateTimeOffset.UtcNow >= deadline) return null;
            progress?.Invoke($"waiting for gms check-in ({(DateTimeOffset.UtcNow - started).TotalSeconds:F0}s)");
            await Task.Delay(interval, ct);
        }
    }

    public static async Task<bool> KickAsync(IDeviceConnection conn, RootAccess root, CancellationToken ct) {
        var r = await conn.ShellAsync(root.Wrap(CheckinCommand), ct);
        return r.Stdout.Contains("kicked=1", StringComparison.Ordinal);
    }

    public static string? Parse(string stdout) {
        foreach (string line in stdout.Split('\n')) {
            if (!line.TrimStart().StartsWith("Row:", StringComparison.Ordinal)) continue;
            int at = line.IndexOf("value=", StringComparison.Ordinal);
            if (at < 0) continue;
            string value = line[(at + "value=".Length)..].Trim();
            if (value.Length > 0 && !value.Equals("NULL", StringComparison.OrdinalIgnoreCase)) return value;
        }

        var pref = CheckinAndroidId().Match(stdout);
        return pref.Success ? pref.Groups[1].Value : null;
    }

    [GeneratedRegex("<string name=\"android_id\">(\\d+)</string>")]
    private static partial Regex CheckinAndroidId();
}
