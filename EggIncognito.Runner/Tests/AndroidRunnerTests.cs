using EggIdentity.Contract;
using EggIncognito.Runner.Adb;
using EggIncognito.Runner.Extract;
using EggIncognito.Runner.Runners;
using EggIncognito.Runner.State;
using Xunit;

namespace EggIncognito.Runner.Tests;

public sealed class AndroidRunnerTests : IDisposable {
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private sealed class FakeAdb : IAdbClient {
        public string Dumpsys = "versionCode=111343\nversionName=1.35.7\n";
        public Task<string> DumpsysPackageAsync(string package, CancellationToken ct) => Task.FromResult(Dumpsys);
        public async Task<string> PullArmApkAsync(string package, string destPath, CancellationToken ct) {
            await File.WriteAllTextAsync(destPath, "apk", ct);
            return destPath;
        }
    }

    private sealed class FakeExtractor : IProtoExtractor {
        public byte[] Bytes = System.Text.Encoding.UTF8.GetBytes("syntax = \"proto2\";\npackage ei;\n");
        public ProtoExtraction Extract(string apkPath) => new(Bytes, "deadbeef");
    }

    private AndroidRunner Make(FakeAdb adb, VersionState state, out List<NewVersionEvent> sent) {
        var captured = new List<NewVersionEvent>();
        sent = captured;
        var cvState = new ClientVersionState(_tmp.Combine($"cv-{Guid.NewGuid():N}"), null);
        return new AndroidRunner(adb, new FakeExtractor(), state, new NullClientVersionReader(), cvState,
            "com.auxbrain.egginc", _tmp.Path, evt => {
                captured.Add(evt);
                return Task.CompletedTask;
            });
    }

    private VersionState FreshState() =>
        new(_tmp.Combine($"st-{Guid.NewGuid():N}"));

    [Fact]
    public async Task NewBuild_Emits_AndSavesState() {
        var runner = Make(new FakeAdb(), FreshState(), out var sent);
        var outcome = await runner.RunOnceAsync(force: false);
        Assert.True(outcome.Emitted);
        Assert.Equal("111343", outcome.Build);
        var evt = Assert.Single(sent);
        Assert.Equal("1.35.7", evt.AppVersion);
        Assert.Equal("111343", evt.Build);
        Assert.Equal("android", evt.Platform);
        Assert.False(string.IsNullOrEmpty(evt.ProtoSha));
        Assert.False(string.IsNullOrEmpty(evt.ProtoTextB64));
        Assert.Null(evt.ClientVersion);
    }

    [Fact]
    public async Task SameBuild_NoForce_DoesNotEmit() {
        var state = FreshState();
        var runner = Make(new FakeAdb(), state, out var sent);
        await runner.RunOnceAsync(force: false);
        sent.Clear();
        var outcome = await runner.RunOnceAsync(force: false);
        Assert.False(outcome.Emitted);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task SameBuild_Force_EmitsAnyway() {
        var state = FreshState();
        var runner = Make(new FakeAdb(), state, out var sent);
        await runner.RunOnceAsync(force: false);
        sent.Clear();
        var outcome = await runner.RunOnceAsync(force: true);
        Assert.True(outcome.Emitted);
        Assert.Single(sent);
    }
}
