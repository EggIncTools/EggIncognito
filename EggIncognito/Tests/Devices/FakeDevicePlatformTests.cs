using EggIncognito.Core;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices;
using EggIncognito.Services.Devices.Fake;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests.Devices;

public class FakeDevicePlatformTests {
    private static readonly Dictionary<string, string> Nothing = [];

    [Fact]
    public void Manifest_UnsupportedEntriesMatchTheRealPlatformsAndSayWhy() {
        var android = FakeStack.Android().Platform.Manifest();
        var ios = FakeStack.Ios().Platform.Manifest();

        Assert.Contains(android, e => e.Name == HarvestEntries.PackageManifest && !e.Supported);
        Assert.Contains(ios, e => e.Name == HarvestEntries.AppPackage && !e.Supported);
        foreach (var entry in android.Concat(ios).Where(e => !e.Supported))
            Assert.False(string.IsNullOrWhiteSpace(entry.UnsupportedNote));
    }

    [Fact]
    public async Task Probe_Unreachable_ReportsNotReachable() {
        var stack = FakeStack.Ios(FakeScenarios.Unreachable);
        var probe = await stack.Platform.ProbeAsync(stack.Target, CancellationToken.None);
        Assert.False(probe.Reachable);
        Assert.Null(probe.InstalledAppVersion);
    }

    [Fact]
    public async Task Probe_Healthy_ReportsTheConfiguredVersion() {
        var stack = FakeStack.Ios();
        var probe = await stack.Platform.ProbeAsync(stack.Target, CancellationToken.None);
        Assert.True(probe.Reachable);
        Assert.Equal(FakeStack.AppVersion, probe.InstalledAppVersion);
        Assert.Equal(FakeStack.Build, probe.InstalledBuild);
    }

    [Fact]
    public async Task Harvest_FailingEntry_FailsExactlyTheTexturesEntry() {
        var stack = FakeStack.Android(FakeScenarios.FailingEntry);
        var outcomes = new Dictionary<string, DeviceOutcome>(StringComparer.Ordinal);
        foreach (var entry in stack.Platform.Manifest().Where(e => e.Supported)) {
            var batch = await stack.Platform.HarvestAsync(stack.Target, entry, Nothing, CancellationToken.None);
            outcomes[entry.Name] = batch.Outcome;
        }

        Assert.Equal(DeviceOutcome.Error, outcomes[HarvestEntries.Textures]);
        foreach ((string name, var outcome) in outcomes) {
            if (name == HarvestEntries.Textures) continue;
            Assert.Equal(DeviceOutcome.Ok, outcome);
        }
    }

    [Fact]
    public async Task Harvest_Unreachable_MovesNothing() {
        var stack = FakeStack.Android(FakeScenarios.Unreachable);
        var entry = stack.Platform.Manifest().First(e => e.Name == HarvestEntries.Meshes);
        var batch = await stack.Platform.HarvestAsync(stack.Target, entry, Nothing, CancellationToken.None);
        Assert.Equal(DeviceOutcome.Unreachable, batch.Outcome);
    }

    [Fact]
    public async Task Harvest_UnsupportedEntry_IsNeverAttempted() {
        var stack = FakeStack.Ios();
        var entry = stack.Platform.Manifest().First(e => !e.Supported);
        var fp = await stack.Platform.FingerprintAsync(stack.Target, entry, CancellationToken.None);
        var batch = await stack.Platform.HarvestAsync(stack.Target, entry, Nothing, CancellationToken.None);
        Assert.Equal(DeviceOutcome.Unsupported, fp.Outcome);
        Assert.Equal(DeviceOutcome.Unsupported, batch.Outcome);
    }

    [Fact]
    public async Task Harvest_KnownShasAreSkipped() {
        var stack = FakeStack.Ios();
        var entry = stack.Platform.Manifest().First(e => e.Name == HarvestEntries.Meshes);

        var first = await stack.Platform.HarvestAsync(stack.Target, entry, Nothing, CancellationToken.None);
        Assert.NotNull(first.Value);
        var known = first.Value!.Items.ToDictionary(i => i.Name, i => Hashes.Sha256Hex(i.Bytes),
            StringComparer.Ordinal);

        var second = await stack.Platform.HarvestAsync(stack.Target, entry, known, CancellationToken.None);
        Assert.NotNull(second.Value);
        Assert.Empty(second.Value!.Items);
        Assert.Equal(first.Value.Present.Count, second.Value.Present.Count);
    }

    [Fact]
    public async Task Fingerprint_IsStableAndNamesTheTier() {
        var stack = FakeStack.Ios();
        var entry = stack.Platform.Manifest().First(e => e.Name == HarvestEntries.Meshes);

        var a = await stack.Platform.FingerprintAsync(stack.Target, entry, CancellationToken.None);
        var b = await stack.Platform.FingerprintAsync(stack.Target, entry, CancellationToken.None);

        Assert.True(a.Ok);
        Assert.Equal(a.Value, b.Value);
        Assert.StartsWith(FakeFixtureTiers.Synthesized, a.Value!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fingerprint_ChangesWhenTheAppVersionChanges() {
        var one = FakeStack.Ios(appVersion: "1.37.1");
        var two = FakeStack.Ios(appVersion: "1.37.2");
        var entry = one.Platform.Manifest().First(e => e.Name == HarvestEntries.Meshes);

        var a = await one.Platform.FingerprintAsync(one.Target, entry, CancellationToken.None);
        var b = await two.Platform.FingerprintAsync(two.Target, entry, CancellationToken.None);

        Assert.NotEqual(a.Value, b.Value);
    }

    [Fact]
    public async Task CaptureParticles_IsUnsupported() {
        var stack = FakeStack.Ios();
        var r = await stack.Platform.CaptureParticlesAsync(stack.Target, "", null, CancellationToken.None);
        Assert.Equal(DeviceOutcome.Unsupported, r.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(r.Note));
    }

    [Fact]
    public async Task ProxyAndCaSurfaces_SucceedTrivially() {
        var stack = FakeStack.Ios();
        Assert.True((await stack.Platform.SetProxyAsync(stack.Target, "10.0.0.1", 9000, CancellationToken.None)).Ok);
        Assert.True((await stack.Platform.ClearProxyAsync(stack.Target, CancellationToken.None)).Ok);
        Assert.True((await stack.Platform.InstallCaAsync(stack.Target, "ca.cer", CancellationToken.None)).Ok);
    }

    [Fact]
    public async Task AppLifecycle_SucceedsForAReachableFake() {
        var stack = FakeStack.Ios();
        Assert.True((await stack.Platform.RestartAppAsync(stack.Target, CancellationToken.None)).Ok);
        Assert.True((await stack.Platform.LockAsync(stack.Target, CancellationToken.None)).Ok);
        Assert.True((await stack.Platform.UnlockAsync(stack.Target, CancellationToken.None)).Ok);
        Assert.True((await stack.Platform.KillAppAsync(stack.Target, CancellationToken.None)).Ok);
    }

    [Fact]
    public async Task UnknownDevice_IsUnreachable() {
        var stack = FakeStack.Ios();
        var stranger = new DeviceTarget("frame-iphone", "ios", "udid", "com.auxbrain.egginc");
        var probe = await stack.Platform.ProbeAsync(stranger, CancellationToken.None);
        Assert.False(probe.Reachable);
    }
}

internal sealed record FakeStack(FakeDevicePlatform Platform, FakeDevice Device, DeviceTarget Target) {
    public const string AppVersion = "1.37.1";
    public const string Build = "1140823";

    public static FakeStack Ios(string scenario = FakeScenarios.Healthy, string appVersion = AppVersion) =>
        Make(Platforms.Ios, scenario, appVersion);

    public static FakeStack Android(string scenario = FakeScenarios.Healthy, string appVersion = AppVersion) =>
        Make(Platforms.Android, scenario, appVersion);

    public static FakeFixtureSource Fixtures() => new(new EmptyScopes());

    private static FakeStack Make(string platform, string scenario, string appVersion) {
        var device = new FakeDevice($"fake-{platform}-0", platform, $"fake {platform}", $"fake:{scenario}",
            "com.auxbrain.egginc", scenario, appVersion, Build, 73, 0);
        var settings = new FakeDeviceSettings([device], 15, 5, 35, 4000);
        var plat = new FakeDevicePlatform(platform, settings, new FakeDeviceVersions(), Fixtures(),
            NullLogger<FakeDevicePlatform>.Instance,
            [new FakeStoreChecker(platform, settings, new FakeDeviceVersions(), Fixtures(),
                new KnownVersionRecorder(new EmptyScopes(), NullLogger<KnownVersionRecorder>.Instance),
                NullLogger<FakeStoreChecker>.Instance)],
            [new FakeProxyConfigurator(platform)],
            [new FakeCaInstaller(platform)],
            [],
            []);
        return new FakeStack(plat, device,
            new DeviceTarget(device.Id, device.Platform, device.Target, device.Package));
    }
}

internal sealed class EmptyScopes : IServiceScopeFactory, IServiceScope, IServiceProvider {
    public IServiceScope CreateScope() => this;
    public IServiceProvider ServiceProvider => this;
    public object? GetService(Type serviceType) => null;
    public void Dispose() => GC.SuppressFinalize(this);
}
