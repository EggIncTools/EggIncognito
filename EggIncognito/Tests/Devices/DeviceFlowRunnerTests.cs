using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class DeviceFlowRunnerTests {
    private static DeviceTarget Target => new("d", "test", "t", "com.auxbrain.egginc");

    private static UiNode TextNode(string? text, string? resourceId = null) =>
        new(resourceId, text, null, "node", null, default, false, true, []);

    private static UiTree Tree(params UiNode[] nodes) {
        var root = new UiNode(null, null, null, "root", null, default, false, true, nodes);
        return new UiTree(root, "");
    }

    [Fact]
    public async Task WaitForText_SucceedsOnLaterDump_PolledMoreThanOnce() {
        var driver = new FakeUiDriver {
            Dumps = i => DeviceResult<UiTree>.Success(
                i == 0 ? Tree(TextNode("still working")) : Tree(TextNode("repair complete")))
        };
        var runner = new DeviceFlowRunner(driver);
        var steps = new[] { DeviceFlowSteps.WaitForText("repair complete", 5, 0) };

        var result = await runner.RunAsync(Target, steps, null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.True(driver.DumpCalls >= 2);
    }

    [Fact]
    public async Task WaitForText_OrAlternatives_MatchesAnyAlternative() {
        var driver = new FakeUiDriver { Dumps = _ => DeviceResult<UiTree>.Success(Tree(TextNode("update complete"))) };
        var runner = new DeviceFlowRunner(driver);
        var steps = new[] { DeviceFlowSteps.WaitForText("repair complete OR update complete", 0, 0) };

        var result = await runner.RunAsync(Target, steps, null, CancellationToken.None);

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task WaitForText_TimesOut_RequiredStepStopsFlow() {
        var driver = new FakeUiDriver { Dumps = _ => DeviceResult<UiTree>.Success(Tree(TextNode("nope"))) };
        var runner = new DeviceFlowRunner(driver);
        var steps = new[] {
            DeviceFlowSteps.WaitForText("done", 0, 0),
            DeviceFlowSteps.Tap(UiSelector.Text("should not run"))
        };

        var result = await runner.RunAsync(Target, steps, null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.NotNull(result.FailedStep);
        Assert.Empty(driver.TapCalls);
    }

    [Fact]
    public async Task OptionalStepFails_LoggedAndFlowContinues() {
        var driver = new FakeUiDriver { Dumps = _ => DeviceResult<UiTree>.Success(Tree()) };
        var runner = new DeviceFlowRunner(driver);
        var steps = new[] {
            DeviceFlowSteps.WaitForSelector(UiSelector.Text("missing"), 0, 0, required: false),
            DeviceFlowSteps.Tap(UiSelector.Text("ok"))
        };

        var result = await runner.RunAsync(Target, steps, null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Null(result.FailedStep);
        Assert.Contains(result.Log, l => l.StartsWith("(optional)", StringComparison.Ordinal));
        Assert.Single(driver.TapCalls);
    }

    [Fact]
    public async Task ReadField_CapturesNodeTextDirectly() {
        var driver = new FakeUiDriver {
            Dumps = _ => DeviceResult<UiTree>.Success(Tree(TextNode("2026-09-01", "id.expiry")))
        };
        var runner = new DeviceFlowRunner(driver);
        var steps = new[] { DeviceFlowSteps.ReadField("expiry", UiSelector.Id("id.expiry")) };

        var result = await runner.RunAsync(Target, steps, null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("2026-09-01", result.Fields["expiry"]);
    }

    [Fact]
    public async Task ReadField_EmptyNodeText_FallsBackToNextNonEmptyTextInPreOrder() {
        var label = TextNode("", "id.expiry");
        var value = TextNode("2026-09-01");
        var driver = new FakeUiDriver { Dumps = _ => DeviceResult<UiTree>.Success(Tree(label, value)) };
        var runner = new DeviceFlowRunner(driver);
        var steps = new[] { DeviceFlowSteps.ReadField("expiry", UiSelector.Id("id.expiry")) };

        var result = await runner.RunAsync(Target, steps, null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("2026-09-01", result.Fields["expiry"]);
    }

    [Fact]
    public async Task ReadField_Required_SelectorNotFound_FailsWithLoggedLineAndStopsFlow() {
        var driver = new FakeUiDriver { Dumps = _ => DeviceResult<UiTree>.Success(Tree()) };
        var runner = new DeviceFlowRunner(driver);
        var steps = new[] {
            DeviceFlowSteps.ReadField("expiry", UiSelector.Id("id.expiry")),
            DeviceFlowSteps.Tap(UiSelector.Text("should not run"))
        };

        var result = await runner.RunAsync(Target, steps, null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("read field expiry", result.FailedStep);
        Assert.NotEmpty(result.Log);
        Assert.Contains(result.Log, l => l.Contains("expiry", StringComparison.Ordinal));
        Assert.Empty(driver.TapCalls);
    }

    [Fact]
    public async Task Screenshot_CollectsShot_NeverFailsFlow() {
        var driver = new FakeUiDriver { ScreenshotResult = DeviceResult<byte[]>.Success([1, 2, 3, 4]) };
        var runner = new DeviceFlowRunner(driver);
        var steps = new[] { DeviceFlowSteps.Screenshot("before") };

        var result = await runner.RunAsync(Target, steps, null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Single(result.Shots);
        Assert.Equal("before", result.Shots[0].Label);
        Assert.Equal(4, result.Shots[0].Png.Length);
    }

    [Fact]
    public async Task Screenshot_DriverError_StillCountsAsOk() {
        var driver = new FakeUiDriver { ScreenshotResult = DeviceResult<byte[]>.Error("no screen") };
        var runner = new DeviceFlowRunner(driver);
        var steps = new[] { DeviceFlowSteps.Screenshot("before") };

        var result = await runner.RunAsync(Target, steps, null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Empty(result.Shots);
        Assert.Contains(result.Log, l => l.Contains("failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tap_CallsDriverTapAsyncWithGivenSelector() {
        var driver = new FakeUiDriver();
        var runner = new DeviceFlowRunner(driver);
        var selector = UiSelector.Text("Repair Mode");
        var steps = new[] { DeviceFlowSteps.Tap(selector) };

        var result = await runner.RunAsync(Target, steps, null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Single(driver.TapCalls);
        Assert.Equal(selector, driver.TapCalls[0]);
    }

    [Fact]
    public async Task Flow_MapsEachStepKindToItsDriverCall() {
        var driver = new FakeUiDriver();
        var runner = new DeviceFlowRunner(driver);
        var steps = new[] {
            DeviceFlowSteps.LaunchApp("com.auxbrain.egginc"),
            DeviceFlowSteps.Key(DeviceKey.Back),
            DeviceFlowSteps.InputText("hello"),
            DeviceFlowSteps.TapPoint(10, 20)
        };

        var result = await runner.RunAsync(Target, steps, null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("com.auxbrain.egginc", Assert.Single(driver.LaunchCalls));
        Assert.Equal(DeviceKey.Back, Assert.Single(driver.KeyCalls));
        Assert.Equal("hello", Assert.Single(driver.InputCalls));
        Assert.Equal((10, 20), Assert.Single(driver.TapPointCalls));
    }

    [Fact]
    public async Task Swipe_CallsDriverSwipeAsyncWithCoordinatesAndDuration() {
        var driver = new FakeUiDriver();
        var runner = new DeviceFlowRunner(driver);
        var steps = new[] { DeviceFlowSteps.Swipe(10, 20, 30, 40, 250) };

        var result = await runner.RunAsync(Target, steps, null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal((10, 20, 30, 40, 250), Assert.Single(driver.SwipeCalls));
        Assert.Contains("swipe (10,20) to (30,40) over 250ms", result.Log);
    }

    [Fact]
    public async Task ExternalCancellation_Rethrows() {
        var driver = new FakeUiDriver { Dumps = _ => DeviceResult<UiTree>.Success(Tree()) };
        var runner = new DeviceFlowRunner(driver);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var steps = new[] { DeviceFlowSteps.WaitForText("x", 5, 5) };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            runner.RunAsync(Target, steps, null, cts.Token));
    }

    private sealed class FakeUiDriver : IDeviceUiDriver {
        public Func<int, DeviceResult<UiTree>> Dumps = _ => DeviceResult<UiTree>.Success(Tree());
        public DeviceResult<byte[]> ScreenshotResult = DeviceResult<byte[]>.Success([]);
        public int DumpCalls;
        public List<UiSelector> TapCalls { get; } = [];
        public List<string> LaunchCalls { get; } = [];
        public List<DeviceKey> KeyCalls { get; } = [];
        public List<string> InputCalls { get; } = [];
        public List<(int X, int Y)> TapPointCalls { get; } = [];

        public string Platform => "test";

        public Task<DeviceResult<UiTree>> DumpAsync(DeviceTarget target, CancellationToken ct) =>
            Task.FromResult(Dumps(DumpCalls++));

        public Task<DeviceResult<byte[]>> ScreenshotAsync(DeviceTarget target, CancellationToken ct) =>
            Task.FromResult(ScreenshotResult);

        public Task<DeviceResult> TapAsync(DeviceTarget target, UiSelector selector, CancellationToken ct) {
            TapCalls.Add(selector);
            return Task.FromResult(DeviceResult.Success());
        }

        public Task<DeviceResult> TapPointAsync(DeviceTarget target, int x, int y, CancellationToken ct) {
            TapPointCalls.Add((x, y));
            return Task.FromResult(DeviceResult.Success());
        }

        public List<(int X1, int Y1, int X2, int Y2, int Ms)> SwipeCalls { get; } = [];

        public Task<DeviceResult> SwipeAsync(DeviceTarget target, int x1, int y1, int x2, int y2, int durationMs,
            CancellationToken ct) {
            SwipeCalls.Add((x1, y1, x2, y2, durationMs));
            return Task.FromResult(DeviceResult.Success());
        }

        public Task<DeviceResult> InputTextAsync(DeviceTarget target, string text, CancellationToken ct) {
            InputCalls.Add(text);
            return Task.FromResult(DeviceResult.Success());
        }

        public Task<DeviceResult> KeyAsync(DeviceTarget target, DeviceKey key, CancellationToken ct) {
            KeyCalls.Add(key);
            return Task.FromResult(DeviceResult.Success());
        }

        public Task<DeviceResult> LaunchAppAsync(DeviceTarget target, string appRef, CancellationToken ct) {
            LaunchCalls.Add(appRef);
            return Task.FromResult(DeviceResult.Success());
        }
    }
}
