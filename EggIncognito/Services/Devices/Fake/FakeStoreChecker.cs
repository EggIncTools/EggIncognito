using System.Globalization;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Fake;

public sealed class FakeStoreChecker(
    string platform,
    FakeDeviceSettings settings,
    FakeDeviceVersions versions,
    FakeFixtureSource fixtures,
    KnownVersionRecorder knownVersions,
    ILogger<FakeStoreChecker> logger) : IDeviceStoreChecker {
    private const int ClimbSteps = 5;
    private const int ClimbStepMs = 2000;
    private const string Source = "fake-store";

    public string Platform => platform;

    public async Task<StoreCheckResult> CheckAndUpdateAsync(DeviceTarget device, CancellationToken ct,
        Func<string, Task>? progress = null) {
        if (settings.For(device.Id) is not { } fake)
            return new StoreCheckResult(false, null, null, false, false, "unreachable", "not a declared fake device");

        if (fake.Scenario == FakeScenarios.Unreachable) {
            return new StoreCheckResult(false, null, null, false, false, "unreachable",
                "fake device is unreachable by scenario");
        }

        (string? before, string? build) = await InstalledAsync(fake, ct);
        if (before is null) {
            return new StoreCheckResult(true, null, null, false, false, "up_to_date",
                "no installed version known for this fake device");
        }

        if (fake.Scenario != FakeScenarios.StoreAhead) {
            await progress.ReportAsync($"installed {before}; fake store reports current");
            return new StoreCheckResult(true, before, before, false, false, "up_to_date",
                "fake store confirms current");
        }

        string after = Bump(before);
        string? afterBuild = BumpBuild(build);
        await progress.ReportAsync($"installed {before}; fake store offers {after}");
        await knownVersions.RecordAsync(platform, after, Source, ct);

        for (int step = 1; step <= ClimbSteps; step++) {
            await Task.Delay(ClimbStepMs, ct);
            await progress.ReportAsync(
                $"installing {after} ({step.ToString(CultureInfo.InvariantCulture)}/{ClimbSteps.ToString(CultureInfo.InvariantCulture)})");
        }

        versions.Set(fake.Id, after, afterBuild);
        logger.LogInformation("fake store: {Id} climbed {Before} -> {After}", fake.Id, before, after);
        return new StoreCheckResult(true, before, after, true, true, "updated",
            $"fake store installed {after} over {before}");
    }

    private async Task<(string? Version, string? Build)> InstalledAsync(FakeDevice fake, CancellationToken ct) {
        var installed = await fixtures.ResolveAsync(fake, versions, ct);
        return (installed.AppVersion, installed.Build);
    }

    internal static string Bump(string version) {
        string[] parts = version.Split('.');
        if (parts.Length > 0 && int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int last)) {
            parts[^1] = (last + 1).ToString(CultureInfo.InvariantCulture);
            return string.Join('.', parts);
        }

        return version + ".1";
    }

    internal static string? BumpBuild(string? build) {
        if (string.IsNullOrEmpty(build)) return build;
        return long.TryParse(build, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n)
            ? (n + 1).ToString(CultureInfo.InvariantCulture)
            : Bump(build);
    }
}
