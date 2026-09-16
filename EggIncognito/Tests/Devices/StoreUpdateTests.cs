using System.Net;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests.Devices;

public class StoreUpdateTests {
    private const string UiWithUpdate =
        "<hierarchy><node text=\"Update\" bounds=\"[718,551][851,608]\"/>" +
        "<node text=\"Uninstall\" bounds=\"[200,551][333,608]\"/></hierarchy>";

    private const string UiNoUpdate =
        "<hierarchy><node text=\"Open\" bounds=\"[718,551][851,608]\"/>" +
        "<node text=\"Uninstall\" bounds=\"[200,551][333,608]\"/></hierarchy>";

    private const string UiMajorUpdate =
        "<hierarchy><node text=\"Play\" bounds=\"[718,551][851,608]\"/>" +
        "<node text=\"Uninstall\" bounds=\"[200,551][333,608]\"/>" +
        "<node text=\"Update available\" bounds=\"[113,1683][376,1728]\"/></hierarchy>";

    private static DeviceTarget AndroidTarget => new("a", "android", "SER", "com.auxbrain.egginc");

    private static DeviceTarget IosTarget => new("i", "ios", "UDID", "com.auxbrain.egginc");

    private static KnownVersionRecorder Recorder() =>
        new(new NullScopeFactory(), NullLogger<KnownVersionRecorder>.Instance);

    private static DeviceActivity Activity() => new(new DeviceClaimRegistry(TimeProvider.System));

    private static StoreUpdateOrchestrator Orchestrator(IStoreUpdateDriver driver, int attempts = 3) =>
        new(driver, new StoreUpdateOrchestrator.Options(0, attempts), Recorder(), NullLogger.Instance);

    private static AndroidStoreCatalog PlayCatalog(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new StubHttpFactory(new StubHttpMessageHandler(respond)), NullLogger<AndroidStoreCatalog>.Instance);

    private static AndroidStoreCatalog NoPlayCatalog() =>
        PlayCatalog(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

    private static AndroidStoreUpdateDriver AndroidDriver(FakeRunner runner, AndroidStoreCatalog catalog) {
        var connections = new FakeConnections(runner);
        return new AndroidStoreUpdateDriver(runner, connections,
            new AndroidStoreUpdateDriver.Options("am start {package}", 0, 0),
            catalog, Recorder(), [new AndroidUiDriver(connections)], Activity(),
            NullLogger<AndroidStoreUpdateDriver>.Instance);
    }

    private static async Task<UiTree> ParseTreeAsync(string xml) {
        var runner = new FakeRunner(args =>
            args.Any(a => a.Contains("cat")) ? new ProcessResult(0, xml, "") : new ProcessResult(0, "", ""));
        var dump = await new AndroidUiDriver(new FakeConnections(runner)).DumpAsync(AndroidTarget, default);
        return dump.Value!;
    }

    private static StoreUpdateOrchestrator AndroidOrchestrator(FakeRunner runner, int attempts = 3) =>
        Orchestrator(AndroidDriver(runner, NoPlayCatalog()), attempts);

    private static HttpResponseMessage Html(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static string PlayPage(string version) =>
        $"<html>null,null,[[[\"{version}\"]],[[[\"Aug 1, 2026\"]]]]</html>";

    private static IosStoreCatalog Catalog(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new StubHttpFactory(new StubHttpMessageHandler(respond)), NullLogger<IosStoreCatalog>.Instance);

    private static IosStoreUpdateDriver IosDriver(IosStoreCatalog catalog) =>
        new(new FakeRunner(_ => new ProcessResult(0, "", "")),
            new IosStoreUpdateDriver.Options(null, "22", null, "/var/mobile/trigger", TweakPath, "12345", null),
            catalog, Recorder(), Activity(), NullLogger<IosStoreUpdateDriver>.Instance);

    private const string TweakPath = "/Library/MobileSubstrate/DynamicLibraries/eggupdate.dylib";

    private static IosStoreUpdateDriver IosSshDriver(FakeRunner runner) =>
        new(runner,
            new IosStoreUpdateDriver.Options("phone", "2222", "/keys/phone", "/var/mobile/trigger", TweakPath,
                "12345", null),
            Catalog(_ => Json("{\"resultCount\":1,\"results\":[{\"version\":\"1.37\"}]}")),
            Recorder(), Activity(), NullLogger<IosStoreUpdateDriver>.Instance);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [Fact]
    public async Task Orchestrator_NoInstalledRead_Unreachable() {
        var driver = new FakeDriver { InstalledReads = _ => null };
        var rounds = new List<string>();

        var result = await Orchestrator(driver).CheckAndUpdateAsync(AndroidTarget, default, msg => rounds.Add(msg));

        Assert.Equal("unreachable", result.Action);
        Assert.False(result.Reachable);
        Assert.False(driver.CleanupCalled);
        Assert.DoesNotContain(rounds, m => m.Contains("waiting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Orchestrator_ProbeUpToDate_ReturnsFast() {
        var driver = new FakeDriver { Probe = new StoreProbeOutcome(StoreAvailability.UpToDate, "1.0", null) };
        var rounds = new List<string>();

        var result = await Orchestrator(driver).CheckAndUpdateAsync(AndroidTarget, default, msg => rounds.Add(msg));

        Assert.Equal("up_to_date", result.Action);
        Assert.False(result.Installed);
        Assert.False(driver.TriggerCalled);
        Assert.True(driver.CleanupCalled);
        Assert.DoesNotContain(rounds, m => m.Contains("waiting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Orchestrator_ProbeManualNeeded_NoTrigger() {
        var driver = new FakeDriver {
            Probe = new StoreProbeOutcome(StoreAvailability.ManualNeeded, null, "needs manual update")
        };

        var result = await Orchestrator(driver).CheckAndUpdateAsync(AndroidTarget, default);

        Assert.Equal("manual_needed", result.Action);
        Assert.True(result.UpdateFound);
        Assert.False(result.Installed);
        Assert.Equal("needs manual update", result.Note);
        Assert.False(driver.TriggerCalled);
        Assert.True(driver.CleanupCalled);
    }

    [Fact]
    public async Task Orchestrator_TriggerFails_Error() {
        var driver = new FakeDriver {
            Probe = new StoreProbeOutcome(StoreAvailability.UpdateOffered, "2.0", null),
            Trigger = new TriggerOutcome(false, "tap failed")
        };

        var result = await Orchestrator(driver).CheckAndUpdateAsync(AndroidTarget, default);

        Assert.Equal("error", result.Action);
        Assert.Equal("tap failed", result.Note);
        Assert.True(result.UpdateFound);
        Assert.False(result.Installed);
        Assert.True(driver.TriggerCalled);
        Assert.True(driver.CleanupCalled);
    }

    [Fact]
    public async Task Orchestrator_UnknownProbe_VersionClimb_Updated() {
        var driver = new FakeDriver { InstalledReads = i => i >= 2 ? "1.1" : "1.0" };
        var rounds = new List<string>();

        var result = await Orchestrator(driver, 10).CheckAndUpdateAsync(AndroidTarget, default, msg => rounds.Add(msg));

        Assert.Equal("updated", result.Action);
        Assert.True(result.Installed);
        Assert.Equal("1.0", result.InstalledBefore);
        Assert.Equal("1.1", result.InstalledAfter);
        Assert.True(driver.TriggerCalled);
        Assert.True(driver.CleanupCalled);
        Assert.Contains(rounds, m => m.Contains("1.1") && m.Contains("1.0"));
    }

    [Fact]
    public async Task Orchestrator_NullProgress_NoThrow() {
        var driver = new FakeDriver { Probe = new StoreProbeOutcome(StoreAvailability.UpToDate, "1.0", null) };

        var result = await Orchestrator(driver).CheckAndUpdateAsync(AndroidTarget, default);

        Assert.Equal("up_to_date", result.Action);
    }

    [Fact]
    public async Task Android_UpToDate_WhenNoUpdateButton() {
        var runner = new FakeRunner(args => {
            return args.Contains("dumpsys")
                ? new ProcessResult(0, "versionName=1.0\n", "")
                : args.Any(a => a.Contains("cat"))
                    ? new ProcessResult(0, UiNoUpdate, "")
                    : new ProcessResult(0, "", "");
        });

        var rounds = new List<string>();
        var result = await AndroidOrchestrator(runner).CheckAndUpdateAsync(AndroidTarget, default, msg => rounds.Add(msg));

        Assert.Equal("up_to_date", result.Action);
        Assert.False(result.Installed);
        Assert.NotEmpty(rounds);
        Assert.DoesNotContain(rounds, m => m.Contains("waiting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Android_MajorUpdateAdvertised_ManualNeeded() {
        var runner = new FakeRunner(args => {
            return args.Contains("dumpsys")
                ? new ProcessResult(0, "versionName=1.0\n", "")
                : args.Any(a => a.Contains("cat"))
                    ? new ProcessResult(0, UiMajorUpdate, "")
                    : new ProcessResult(0, "", "");
        });

        var result = await AndroidOrchestrator(runner).CheckAndUpdateAsync(AndroidTarget, default);

        Assert.Equal("manual_needed", result.Action);
        Assert.True(result.UpdateFound);
        Assert.False(result.Installed);
    }

    [Fact]
    public async Task Android_PageNeverLoads_Error() {
        var runner = new FakeRunner(args => {
            return args.Contains("dumpsys")
                ? new ProcessResult(0, "versionName=1.0\n", "")
                : new ProcessResult(0, "", "");
        });

        var result = await AndroidOrchestrator(runner, 2).CheckAndUpdateAsync(AndroidTarget, default);

        Assert.Equal("error", result.Action);
    }

    [Fact]
    public async Task Android_ProgressAnnouncesClimb_ThenUpdated() {
        int dumpsys = 0;
        var runner = new FakeRunner(args => {
            if (args.Contains("dumpsys")) {
                string v = dumpsys++ >= 2 ? "1.1" : "1.0";
                return new ProcessResult(0, $"versionName={v}\n", "");
            }

            return args.Any(a => a.Contains("cat"))
                ? new ProcessResult(0, UiWithUpdate, "")
                : new ProcessResult(0, "", "");
        });

        var rounds = new List<string>();
        var result = await AndroidOrchestrator(runner, 10).CheckAndUpdateAsync(AndroidTarget, default, msg => rounds.Add(msg));

        Assert.Equal("updated", result.Action);
        Assert.True(result.Installed);
        Assert.Equal("1.0", result.InstalledBefore);
        Assert.Equal("1.1", result.InstalledAfter);
        Assert.Contains(rounds, m => m.Contains("1.1") && m.Contains("1.0"));
    }

    [Fact]
    public async Task Android_EmptyDumpsys_Unreachable() {
        var runner = new FakeRunner(_ => new ProcessResult(0, "", ""));
        var rounds = new List<string>();

        var result = await AndroidOrchestrator(runner).CheckAndUpdateAsync(AndroidTarget, default, msg => rounds.Add(msg));

        Assert.Equal("unreachable", result.Action);
        Assert.False(result.Reachable);
        Assert.DoesNotContain(rounds, m => m.Contains("waiting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TriggerInstallAsync_TapsUpdateNodeCenter_ViaTapPointAsync() {
        var tree = await ParseTreeAsync(UiWithUpdate);
        var ui = new FakeUiDriver { DumpResult = DeviceResult<UiTree>.Success(tree) };
        var logger = new CollectingLogger();
        var runner = new FakeRunner(_ => new ProcessResult(0, "", ""));
        var driver = new AndroidStoreUpdateDriver(
            runner, new FakeConnections(runner),
            new AndroidStoreUpdateDriver.Options("am start {package}", 0, 0),
            NoPlayCatalog(), Recorder(), [ui], Activity(), logger);

        var trig = await driver.TriggerInstallAsync(AndroidTarget, null, default);

        Assert.True(trig.Ok);
        Assert.True(ui.TapPointCalled);
        Assert.False(ui.TapCalled);
        Assert.Equal((784, 579), ui.LastTapPoint);
        Assert.Contains(logger.Messages, m => m == "device check-update: a android tapped Update at 784,579");
    }

    [Fact]
    public async Task TriggerInstallAsync_NoUpdateNode_FailsWithoutTapping() {
        var tree = await ParseTreeAsync(UiNoUpdate);
        var ui = new FakeUiDriver { DumpResult = DeviceResult<UiTree>.Success(tree) };
        var runner = new FakeRunner(_ => new ProcessResult(0, "", ""));
        var driver = new AndroidStoreUpdateDriver(
            runner, new FakeConnections(runner),
            new AndroidStoreUpdateDriver.Options("am start {package}", 0, 0),
            NoPlayCatalog(), Recorder(), [ui], Activity(), NullLogger<AndroidStoreUpdateDriver>.Instance);

        var trig = await driver.TriggerInstallAsync(AndroidTarget, null, default);

        Assert.False(trig.Ok);
        Assert.Equal("no Update button on the Play page", trig.Note);
        Assert.False(ui.TapPointCalled);
    }

    [Fact]
    public async Task TriggerInstallAsync_NoUiDriverRegistered_ReportsCouldNotDump() {
        var runner = new FakeRunner(_ => new ProcessResult(0, "", ""));
        var driver = new AndroidStoreUpdateDriver(
            runner, new FakeConnections(runner),
            new AndroidStoreUpdateDriver.Options("am start {package}", 0, 0),
            NoPlayCatalog(), Recorder(), [], Activity(), NullLogger<AndroidStoreUpdateDriver>.Instance);

        var trig = await driver.TriggerInstallAsync(AndroidTarget, null, default);

        Assert.False(trig.Ok);
        Assert.Equal("could not dump Play UI", trig.Note);
    }

    [Fact]
    public void PlayCatalog_ParsesVersionToken() =>
        Assert.Equal("1.37", AndroidStoreCatalog.ParseVersion(PlayPage("1.37")));

    [Fact]
    public void PlayCatalog_ParsesThreePartVersion() =>
        Assert.Equal("1.37.2", AndroidStoreCatalog.ParseVersion(PlayPage("1.37.2")));

    [Fact]
    public void PlayCatalog_NoToken_ReturnsNull() =>
        Assert.Null(AndroidStoreCatalog.ParseVersion("<html>Varies with device</html>"));

    [Fact]
    public async Task PlayCatalog_HttpError_ReturnsNull() {
        var catalog = PlayCatalog(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        Assert.Null(await catalog.LatestVersionAsync("com.auxbrain.egginc", null, "en", default));
    }

    [Fact]
    public async Task PlayCatalog_SendsLocaleAndCountry() {
        Uri? seen = null;
        var catalog = PlayCatalog(req => {
            seen = req.RequestUri;
            return Html(PlayPage("1.37"));
        });

        Assert.Equal("1.37", await catalog.LatestVersionAsync("com.auxbrain.egginc", "US", "en", default));
        Assert.Contains("id=com.auxbrain.egginc", seen!.Query, StringComparison.Ordinal);
        Assert.Contains("hl=en", seen.Query, StringComparison.Ordinal);
        Assert.Contains("gl=US", seen.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AndroidProbe_PlayMatchesButDeviceOffersUpdate_UpdateOffered() {
        var runner = new FakeRunner(args =>
            args.Any(a => a.Contains("cat")) ? new ProcessResult(0, UiWithUpdate, "") : new ProcessResult(0, "", ""));
        var driver = AndroidDriver(runner, PlayCatalog(_ => Html(PlayPage("1.37"))));

        var probe = await driver.ProbeStoreAsync(AndroidTarget, "1.37", null, default);

        Assert.Equal(StoreAvailability.UpdateOffered, probe.Availability);
        Assert.Equal("1.37", probe.StoreVersion);
    }

    [Fact]
    public async Task AndroidProbe_PlayMatchesAndNoUpdateButton_UpToDate() {
        var runner = new FakeRunner(args =>
            args.Any(a => a.Contains("cat")) ? new ProcessResult(0, UiNoUpdate, "") : new ProcessResult(0, "", ""));
        var driver = AndroidDriver(runner, PlayCatalog(_ => Html(PlayPage("1.37"))));

        var probe = await driver.ProbeStoreAsync(AndroidTarget, "1.37", null, default);

        Assert.Equal(StoreAvailability.UpToDate, probe.Availability);
        Assert.Equal("1.37", probe.StoreVersion);
    }

    [Fact]
    public async Task AndroidProbe_PlayAhead_UpdateOfferedWithStoreVersion() {
        var runner = new FakeRunner(args =>
            args.Any(a => a.Contains("cat")) ? new ProcessResult(0, UiWithUpdate, "") : new ProcessResult(0, "", ""));
        var driver = AndroidDriver(runner, PlayCatalog(_ => Html(PlayPage("1.37"))));

        var probe = await driver.ProbeStoreAsync(AndroidTarget, "1.36", null, default);

        Assert.Equal(StoreAvailability.UpdateOffered, probe.Availability);
        Assert.Equal("1.37", probe.StoreVersion);
    }

    [Fact]
    public async Task AndroidProbe_PlayAheadButNoButton_ManualNeeded() {
        var runner = new FakeRunner(args =>
            args.Any(a => a.Contains("cat")) ? new ProcessResult(0, UiNoUpdate, "") : new ProcessResult(0, "", ""));
        var driver = AndroidDriver(runner, PlayCatalog(_ => Html(PlayPage("1.37"))));

        var probe = await driver.ProbeStoreAsync(AndroidTarget, "1.36", null, default);

        Assert.Equal(StoreAvailability.ManualNeeded, probe.Availability);
        Assert.Equal("1.37", probe.StoreVersion);
    }

    [Fact]
    public async Task AndroidProbe_PlayUnavailable_FallsBackToUi() {
        var runner = new FakeRunner(args =>
            args.Any(a => a.Contains("cat")) ? new ProcessResult(0, UiNoUpdate, "") : new ProcessResult(0, "", ""));
        var driver = AndroidDriver(runner, NoPlayCatalog());

        var probe = await driver.ProbeStoreAsync(AndroidTarget, "1.36", null, default);

        Assert.Equal(StoreAvailability.UpToDate, probe.Availability);
        Assert.Null(probe.StoreVersion);
    }

    [Fact]
    public async Task Catalog_ValidLookup_ReturnsVersion() {
        var catalog = Catalog(_ => Json("{\"resultCount\":1,\"results\":[{\"version\":\"1.37\"}]}"));
        Assert.Equal("1.37", await catalog.LatestVersionAsync("12345", null, default));
    }

    [Fact]
    public async Task Catalog_NoResults_ReturnsNull() {
        var catalog = Catalog(_ => Json("{\"resultCount\":0,\"results\":[]}"));
        Assert.Null(await catalog.LatestVersionAsync("12345", null, default));
    }

    [Fact]
    public async Task Catalog_HttpError_ReturnsNull() {
        var catalog = Catalog(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        Assert.Null(await catalog.LatestVersionAsync("12345", null, default));
    }

    [Fact]
    public async Task Catalog_MalformedJson_ReturnsNull() {
        var catalog = Catalog(_ => Json("not json at all"));
        Assert.Null(await catalog.LatestVersionAsync("12345", null, default));
    }

    [Fact]
    public async Task IosProbe_StoreMatchesInstalled_UpToDate() {
        var driver = IosDriver(Catalog(_ => Json("{\"resultCount\":1,\"results\":[{\"version\":\"1.36\"}]}")));

        var probe = await driver.ProbeStoreAsync(IosTarget, "1.36", null, default);

        Assert.Equal(StoreAvailability.UpToDate, probe.Availability);
        Assert.Equal("1.36", probe.StoreVersion);
    }

    [Fact]
    public async Task IosProbe_StoreAhead_UpdateOffered() {
        var driver = IosDriver(Catalog(_ => Json("{\"resultCount\":1,\"results\":[{\"version\":\"1.37\"}]}")));

        var probe = await driver.ProbeStoreAsync(IosTarget, "1.36", null, default);

        Assert.Equal(StoreAvailability.UpdateOffered, probe.Availability);
        Assert.Equal("1.37", probe.StoreVersion);
    }

    [Fact]
    public async Task IosTrigger_TweakMissing_FailsWithoutTouchingTrigger() {
        var seen = new List<string>();
        var runner = new FakeRunner(args => {
            seen.Add(args[^1]);
            return new ProcessResult(0, "tweak-absent\n", "");
        });

        var trig = await IosSshDriver(runner).TriggerInstallAsync(IosTarget, null, default);

        Assert.False(trig.Ok);
        Assert.Contains("eggupdate tweak not installed", trig.Note, StringComparison.Ordinal);
        Assert.DoesNotContain(seen, c => c.StartsWith("touch ", StringComparison.Ordinal));
        Assert.DoesNotContain(seen, c => c.Contains("uiopen", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IosTrigger_TweakPresent_PrimesThenTouchesTrigger() {
        var seen = new List<string>();
        var runner = new FakeRunner(args => {
            seen.Add(args[^1]);
            return new ProcessResult(0, "tweak-present\n", "");
        });

        var trig = await IosSshDriver(runner).TriggerInstallAsync(IosTarget, null, default);

        Assert.True(trig.Ok);
        Assert.Contains(seen, c => c.Contains("uiopen", StringComparison.Ordinal));
        Assert.Contains(seen, c => c.StartsWith("touch /var/mobile/trigger", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IosTrigger_SshUnusable_ReportsVerifyFailure() {
        var runner = new FakeRunner(_ => new ProcessResult(255, "", "ssh: connect to host phone port 2222: timed out"));

        var trig = await IosSshDriver(runner).TriggerInstallAsync(IosTarget, null, default);

        Assert.False(trig.Ok);
        Assert.Contains("could not verify the eggupdate tweak", trig.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IosProbe_LookupFails_Unknown() {
        var driver = IosDriver(Catalog(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var probe = await driver.ProbeStoreAsync(IosTarget, "1.36", null, default);

        Assert.Equal(StoreAvailability.Unknown, probe.Availability);
        Assert.Null(probe.StoreVersion);
    }

    private sealed class FakeDriver : IStoreUpdateDriver {
        public Func<int, string?> InstalledReads = _ => "1.0";
        public StoreProbeOutcome Probe = new(StoreAvailability.Unknown, null, null);
        public TriggerOutcome Trigger = new(true, null);
        public bool TriggerCalled;
        public bool CleanupCalled;
        private int _reads;

        public string Platform => "test";
        public string StoreName => "Store";

        public Task<string?> ReadInstalledAsync(DeviceTarget target, CancellationToken ct) =>
            Task.FromResult(InstalledReads(_reads++));

        public Task PrepareAsync(DeviceTarget target, CancellationToken ct) => Task.CompletedTask;

        public Task<StoreProbeOutcome> ProbeStoreAsync(
            DeviceTarget target, string installed, Action<string>? progress, CancellationToken ct) =>
            Task.FromResult(Probe);

        public Task<TriggerOutcome> TriggerInstallAsync(
            DeviceTarget target, Action<string>? progress, CancellationToken ct) {
            TriggerCalled = true;
            return Task.FromResult(Trigger);
        }

        public Task CleanupAsync(DeviceTarget target, CancellationToken ct) {
            CleanupCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRunner(Func<string[], ProcessResult> fn) : IProcessRunner {
        public Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct) =>
            Task.FromResult(fn(args));
    }

    private sealed class FakeConnections(IProcessRunner runner) : IDeviceConnectionFactory {
        public IDeviceConnection? For(DeviceTarget target) => new AdbDeviceConnection(runner, target.Target);
        public SshDeviceConnection? Ios(string? hostFallback = null) => null;
    }

    private sealed class FakeUiDriver : IDeviceUiDriver {
        public DeviceResult<UiTree> DumpResult = DeviceResult<UiTree>.Error("not set");
        public DeviceResult TapPointResult = DeviceResult.Success();
        public bool TapPointCalled;
        public bool TapCalled;
        public (int X, int Y)? LastTapPoint;

        public string Platform => Platforms.Android;

        public Task<DeviceResult<UiTree>> DumpAsync(DeviceTarget target, CancellationToken ct) =>
            Task.FromResult(DumpResult);

        public Task<DeviceResult<byte[]>> ScreenshotAsync(DeviceTarget target, CancellationToken ct) =>
            Task.FromResult(DeviceResult<byte[]>.Unsupported());

        public Task<DeviceResult> TapAsync(DeviceTarget target, UiSelector selector, CancellationToken ct) {
            TapCalled = true;
            return Task.FromResult(DeviceResult.Success());
        }

        public Task<DeviceResult> TapPointAsync(DeviceTarget target, int x, int y, CancellationToken ct) {
            TapPointCalled = true;
            LastTapPoint = (x, y);
            return Task.FromResult(TapPointResult);
        }

        public Task<DeviceResult> InputTextAsync(DeviceTarget target, string text, CancellationToken ct) =>
            Task.FromResult(DeviceResult.Unsupported());

        public Task<DeviceResult> KeyAsync(DeviceTarget target, DeviceKey key, CancellationToken ct) =>
            Task.FromResult(DeviceResult.Unsupported());

        public Task<DeviceResult> LaunchAppAsync(DeviceTarget target, string appRef, CancellationToken ct) =>
            Task.FromResult(DeviceResult.Unsupported());
    }

    private sealed class CollectingLogger : ILogger<AndroidStoreUpdateDriver> {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class NullScopeFactory : IServiceScopeFactory {
        public IServiceScope CreateScope() => new NullScope();

        private sealed class NullScope : IServiceScope {
            public IServiceProvider ServiceProvider { get; } = new NullProvider();

            public void Dispose() {
            }
        }

        private sealed class NullProvider : IServiceProvider {
            public object? GetService(Type serviceType) => null;
        }
    }
}
