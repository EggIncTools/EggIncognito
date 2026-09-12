using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices.Fake;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests.Devices;

public class HarvestManifestTests {
    private sealed class DeadRunner : IProcessRunner {
        public Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct) =>
            Task.FromResult(new ProcessResult(1, "", "no device"));
    }

    private static AndroidPlatform Android() =>
        new(new DeadRunner(), new ConfigurationBuilder().Build(), [], [], [], [], [],
            NullLogger<AndroidPlatform>.Instance);

    private static IosPlatform Ios() {
        var runner = new DeadRunner();
        var config = new DeviceCaptureConfig();
        return new IosPlatform(new DeviceConnectionFactory(runner, config), config, runner, [], [], [], [], [],
            NullLogger<IosPlatform>.Instance);
    }

    private static FakeDevicePlatform FakeAndroid() => FakeStack.Android().Platform;

    private static FakeDevicePlatform FakeIos() => FakeStack.Ios().Platform;

    private static IReadOnlyList<HarvestEntry>[] AllManifests() =>
        [Android().Manifest(), Ios().Manifest(), FakeAndroid().Manifest(), FakeIos().Manifest()];

    [Fact]
    public void EveryPlatform_DeclaresTheSameEntryNames() {
        var expected = Android().Manifest().Select(e => e.Name).Order(StringComparer.Ordinal).ToList();
        foreach (var manifest in AllManifests()) {
            List<string> names = [.. manifest.Select(e => e.Name).Order(StringComparer.Ordinal)];
            Assert.Equal(expected, names);
        }
    }

    [Fact]
    public void FakePlatforms_MirrorTheirRealCounterpartsSupportFlags() {
        Assert.Equal(
            Android().Manifest().Select(e => (e.Name, e.Supported)).Order(),
            FakeAndroid().Manifest().Select(e => (e.Name, e.Supported)).Order());
        Assert.Equal(
            Ios().Manifest().Select(e => (e.Name, e.Supported)).Order(),
            FakeIos().Manifest().Select(e => (e.Name, e.Supported)).Order());
    }

    [Fact]
    public void EveryEntry_HasAKindAndNoDuplicates() {
        foreach (var manifest in AllManifests()) {
            Assert.NotEmpty(manifest);
            Assert.All(manifest, e => Assert.False(string.IsNullOrWhiteSpace(e.Kind)));
            Assert.Equal(manifest.Count, manifest.Select(e => e.Name).Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void UnsupportedEntries_SayWhy() {
        foreach (var manifest in AllManifests()) {
            foreach (var entry in manifest.Where(e => !e.Supported))
                Assert.False(string.IsNullOrWhiteSpace(entry.UnsupportedNote));
        }
    }

    [Fact]
    public void EveryPlatform_HarvestsTheAppBinaryAndMeshes() {
        foreach (var manifest in AllManifests()) {
            Assert.Contains(manifest, e => e.Name == HarvestEntries.AppBinary && e.Supported);
            Assert.Contains(manifest, e => e.Name == HarvestEntries.Meshes && e.Supported);
        }
    }

    [Fact]
    public async Task UnsupportedEntry_IsNeverAttempted() {
        var android = Android();
        var entry = android.Manifest().First(e => !e.Supported);
        var target = new DeviceTarget("x", "android", "127.0.0.1:5555", "com.auxbrain.egginc");

        var fp = await android.FingerprintAsync(target, entry, default);
        var batch = await android.HarvestAsync(target, entry, new Dictionary<string, string>(), default);

        Assert.Equal(DeviceOutcome.Unsupported, fp.Outcome);
        Assert.Equal(DeviceOutcome.Unsupported, batch.Outcome);
    }
}
