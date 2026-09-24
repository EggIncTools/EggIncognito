using System.Globalization;

namespace EggIncognito.Runner;

public sealed record RunnerOptions(
    string Package,
    string ApkStashDir,
    string DevicesDir,
    int PollIntervalSeconds,
    string EventUrl,
    string EventSecret,
    string TriggerSecret,
    string TriggerUrls,
    string IosBinaryPath,
    int? PreviousClientVersion) {
    public static RunnerOptions FromEnvironment() {
        string apkStash = Env("APK_STASH_DIR", "apks");
        return new RunnerOptions(
            Env("PACKAGE", "com.auxbrain.egginc"),
            apkStash,
            Env("DEVICES_DIR"),
            int.TryParse(Env("POLL_INTERVAL"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int interval)
                ? interval
                : 300,
            Env("SYNC_EVENT_URL"),
            Env("SYNC_EVENT_SECRET"),
            Env("RUNNER_TRIGGER_SECRET"),
            Env("RUNNER_TRIGGER_URLS", "http://127.0.0.1:5055"),
            Env("IOS_BINARY_PATH", Path.Combine(apkStash, "ios-binary")),
            int.TryParse(Env("PREV_CLIENT_VERSION"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int previous)
                ? previous
                : null);
    }

    public static string Env(string key, string fallback = "") =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } value ? value : fallback;
}
