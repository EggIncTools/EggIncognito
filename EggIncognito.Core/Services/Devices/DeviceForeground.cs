namespace EggIncognito.Core.Services.Devices;

public sealed record ForegroundWindow(string? Package, string? Component, string Raw) {
    public bool Is(string package) =>
        Package is { Length: > 0 } p && p.Equals(package, StringComparison.OrdinalIgnoreCase);
}

public static class DeviceForeground {
    public const string PlayStorePackage = "com.android.vending";

    public const string PlayBlockNote =
        "Google Play holds the foreground; the device is not Play Protect certified, "
        + "so Play refuses to let the app run";

    public const string FocusCommand =
        "dumpsys window 2>/dev/null | grep -E \"mCurrentFocus|mFocusedApp\" | head -n 2";

    public const string CloseForegroundCommand =
        "p=$(" + FocusCommand + " | grep -oE \"[A-Za-z][A-Za-z0-9_.]*/\" | head -n 1 | tr -d /); "
        + "h=$(cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.HOME "
        + "2>/dev/null | tail -n 1 | cut -d/ -f1); "
        + "if [ -z \"$p\" ]; then echo \"no focused app\"; exit 1; fi; "
        + "if [ \"$p\" = \"$h\" ] || [ \"$p\" = com.android.systemui ]; then echo \"$p is not an app\"; exit 1; fi; "
        + "am force-stop \"$p\" && echo \"closed $p\"";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(20);

    public static async Task<ForegroundWindow> ReadAsync(IDeviceConnection conn, CancellationToken ct) {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(QueryTimeout);
        var r = await conn.ShellAsync(FocusCommand, bounded.Token);
        ct.ThrowIfCancellationRequested();
        return Parse(r.Stdout);
    }

    public static async Task<ForegroundWindow> WaitAsync(
        IDeviceConnection conn, string package, string blocker, TimeSpan timeout, CancellationToken ct) {
        var deadline = DateTimeOffset.UtcNow + timeout;
        ForegroundWindow front;
        while (true) {
            front = await ReadAsync(conn, ct);
            if (front.Is(package) || front.Is(blocker) || DateTimeOffset.UtcNow >= deadline) return front;
            await Task.Delay(PollInterval, ct);
        }
    }

    public static ForegroundWindow Parse(string stdout) {
        string raw = DeviceParsing.TrimNote(stdout);
        foreach (string line in stdout.Split('\n')) {
            foreach (string token in line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)) {
                string t = token.Trim('{', '}', ',');
                int slash = t.IndexOf('/', StringComparison.Ordinal);
                if (slash <= 0 || !t[..slash].Contains('.', StringComparison.Ordinal)) continue;
                return new ForegroundWindow(t[..slash], t, raw);
            }
        }

        return new ForegroundWindow(null, null, raw);
    }
}
