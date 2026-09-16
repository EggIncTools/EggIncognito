using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class IosUiDriverTests {
    private const string SampleJson =
        "{\"class\":\"UIWindow\",\"label\":null,\"id\":null,\"text\":null," +
        "\"frame\":{\"x\":0,\"y\":0,\"w\":393,\"h\":852},\"enabled\":true,\"children\":[" +
        "{\"class\":\"UIButton\",\"label\":\"Play\",\"id\":\"play_button\",\"text\":\"PLAY\"," +
        "\"frame\":{\"x\":40,\"y\":700,\"w\":313,\"h\":56},\"enabled\":true,\"children\":[]}]}";

    private static DeviceTarget IosTarget => new("i", "ios", "phone", "com.auxbrain.egginc");

    private static IosUiDriver.Options DefaultOptions =>
        new("/Library/MobileSubstrate/DynamicLibraries/egiuinav.dylib");

    private static IosUiDriver.Options FastTimeoutOptions =>
        new("/Library/MobileSubstrate/DynamicLibraries/egiuinav.dylib", PollIntervalMs: 5, TimeoutMs: 30);

    [Fact]
    public void ParseTree_RootAndChild_MapFieldsBoundsAndCenter() {
        var root = IosUiDriver.ParseTree(SampleJson);

        Assert.Equal("UIWindow", root.ClassName);
        Assert.Null(root.ContentDesc);
        Assert.Null(root.ResourceId);
        Assert.Null(root.Text);
        Assert.Equal(new UiBounds(0, 0, 393, 852), root.Bounds);
        Assert.True(root.Enabled);
        Assert.True(root.Clickable);
        Assert.Single(root.Children);

        var child = root.Children[0];
        Assert.Equal("UIButton", child.ClassName);
        Assert.Equal("Play", child.ContentDesc);
        Assert.Equal("play_button", child.ResourceId);
        Assert.Equal("PLAY", child.Text);
        Assert.Equal(new UiBounds(40, 700, 353, 756), child.Bounds);
        Assert.Equal(196, child.Bounds.CenterX);
        Assert.Equal(728, child.Bounds.CenterY);
        Assert.True(child.Enabled);
        Assert.True(child.Clickable);
        Assert.Empty(child.Children);
    }

    [Fact]
    public void ParseTree_Selectors_ResolveByTextAndId() {
        var tree = new UiTree(IosUiDriver.ParseTree(SampleJson), SampleJson);

        var byText = UiSelector.Resolve(tree, UiSelector.Text("PLAY"));
        var byId = UiSelector.Resolve(tree, UiSelector.Id("play_button"));

        Assert.NotNull(byText);
        Assert.Same(byText, byId);
        Assert.Equal("Play", byText!.ContentDesc);
    }

    [Fact]
    public async Task DumpAsync_TweakAbsent_ReturnsUnsupported() {
        var runner = new FakeRunner((_, args) => Presence(args, present: false));
        var driver = new IosUiDriver(new FakeConnections(runner), DefaultOptions);

        var result = await driver.DumpAsync(IosTarget, default);

        Assert.Equal(DeviceOutcome.Unsupported, result.Outcome);
        Assert.Contains("egi-uinav tweak not installed", result.Note);
    }

    [Fact]
    public async Task DumpAsync_SshNotConfigured_ReturnsUnreachable() {
        var runner = new FakeRunner((_, _) => new ProcessResult(0, "", ""));
        var driver = new IosUiDriver(new FakeConnections(runner, sshConfigured: false), DefaultOptions);

        var result = await driver.DumpAsync(IosTarget, default);

        Assert.Equal(DeviceOutcome.Unreachable, result.Outcome);
        Assert.Equal("ios ssh not configured", result.Note);
    }

    [Fact]
    public async Task DumpAsync_Success_PullsAndParsesJson() {
        var runner = HappyRunner("ok dump nodes=2", jsonBytes: System.Text.Encoding.UTF8.GetBytes(SampleJson));
        var driver = new IosUiDriver(new FakeConnections(runner), DefaultOptions);

        var result = await driver.DumpAsync(IosTarget, default);

        Assert.True(result.Ok);
        Assert.NotNull(result.Value);
        Assert.Equal("UIButton", result.Value!.Nodes().Last().ClassName);
    }

    [Fact]
    public async Task ScreenshotAsync_Success_ReturnsBytes() {
        byte[] png = [1, 2, 3, 4];
        var runner = HappyRunner("ok screenshot bytes=4", pngBytes: png);
        var driver = new IosUiDriver(new FakeConnections(runner), DefaultOptions);

        var result = await driver.ScreenshotAsync(IosTarget, default);

        Assert.True(result.Ok);
        Assert.Equal(png, result.Value);
    }

    [Fact]
    public async Task TapPointAsync_Success_SendsQuotedTapCommand() {
        var runner = HappyRunner("ok tap");
        var driver = new IosUiDriver(new FakeConnections(runner), DefaultOptions);

        var result = await driver.TapPointAsync(IosTarget, 100, 200, default);

        Assert.True(result.Ok);
        Assert.Contains(runner.Calls,
            c => c.Exe == "ssh" && c.Args[^1].Contains("printf %s 'tap 100 200'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InputTextAsync_PreservesInternalSpaces() {
        var runner = HappyRunner("ok text");
        var driver = new IosUiDriver(new FakeConnections(runner), DefaultOptions);

        var result = await driver.InputTextAsync(IosTarget, "hello world", default);

        Assert.True(result.Ok);
        Assert.Contains(runner.Calls,
            c => c.Exe == "ssh" && c.Args[^1].Contains("printf %s 'text hello world'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeyAsync_Home_SendsKeyHomeCommand() {
        var runner = HappyRunner("ok key home");
        var driver = new IosUiDriver(new FakeConnections(runner), DefaultOptions);

        var result = await driver.KeyAsync(IosTarget, DeviceKey.Home, default);

        Assert.True(result.Ok);
        Assert.Contains(runner.Calls,
            c => c.Exe == "ssh" && c.Args[^1].Contains("printf %s 'key home'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(DeviceKey.Back)]
    [InlineData(DeviceKey.Enter)]
    [InlineData(DeviceKey.Wake)]
    [InlineData(DeviceKey.Sleep)]
    [InlineData(DeviceKey.DismissKeyguard)]
    public async Task KeyAsync_UnsupportedKeys_ReturnUnsupported(DeviceKey key) {
        var runner = new FakeRunner((_, _) => new ProcessResult(0, "", ""));
        var driver = new IosUiDriver(new FakeConnections(runner), DefaultOptions);

        var result = await driver.KeyAsync(IosTarget, key, default);

        Assert.Equal(DeviceOutcome.Unsupported, result.Outcome);
        Assert.Contains(key.ToString(), result.Note);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task RunAsync_DoneNeverAppears_ReturnsUnreachableTimeout() {
        var runner = new FakeRunner((_, args) => Presence(args, present: true, doneLine: null));
        var driver = new IosUiDriver(new FakeConnections(runner), FastTimeoutOptions);

        var result = await driver.TapPointAsync(IosTarget, 1, 1, default);

        Assert.Equal(DeviceOutcome.Unreachable, result.Outcome);
        Assert.Contains("did not respond", result.Note);
    }

    [Fact]
    public async Task RunAsync_ErrLine_ReturnsError() {
        var runner = new FakeRunner((_, args) => Presence(args, present: true, doneLine: "err no-key-window"));
        var driver = new IosUiDriver(new FakeConnections(runner), DefaultOptions);

        var result = await driver.TapPointAsync(IosTarget, 1, 1, default);

        Assert.Equal(DeviceOutcome.Error, result.Outcome);
        Assert.Equal("err no-key-window", result.Note);
    }

    [Theory]
    [InlineData("ok state awake=1 locked=0", true, false)]
    [InlineData("ok state awake=0 locked=1", false, true)]
    [InlineData("ok state awake=unknown locked=0", true, false)]
    [InlineData("ok state awake=1 locked=unknown", true, false)]
    [InlineData("ok state", true, false)]
    [InlineData("ok state awake locked", true, false)]
    public void ParseScreenState_MapsFlagsAndUnknownsConservatively(string done, bool awake, bool locked) {
        var state = IosUiDriver.ParseScreenState(done);

        Assert.Equal(awake, state.Awake);
        Assert.Equal(locked, state.Locked);
    }

    [Fact]
    public async Task ScreenStateAsync_Success_SendsStateCommandAndParses() {
        var runner = HappyRunner("ok state awake=0 locked=1");
        var driver = new IosUiDriver(new FakeConnections(runner), DefaultOptions);

        var result = await driver.ScreenStateAsync(IosTarget, default);

        Assert.True(result.Ok);
        Assert.False(result.Value.Awake);
        Assert.True(result.Value.Locked);
        Assert.Contains(runner.Calls,
            c => c.Exe == "ssh" && c.Args[^1].Contains("printf %s 'state'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScreenStateAsync_TweakAbsent_ReturnsUnsupported() {
        var runner = new FakeRunner((_, args) => Presence(args, present: false));
        var driver = new IosUiDriver(new FakeConnections(runner), DefaultOptions);

        var result = await driver.ScreenStateAsync(IosTarget, default);

        Assert.Equal(DeviceOutcome.Unsupported, result.Outcome);
        Assert.Contains("egi-uinav tweak not installed", result.Note);
    }

    [Theory]
    [InlineData("ok frontmost bundle=com.auxbrain.egginc", "com.auxbrain.egginc")]
    [InlineData("ok frontmost bundle=", "")]
    [InlineData("ok frontmost", "")]
    [InlineData("ok frontmost bundle", "")]
    public void ParseFrontmost_ReadsBundleOrEmpty(string done, string expected) =>
        Assert.Equal(expected, IosUiDriver.ParseFrontmost(done));

    [Fact]
    public async Task FrontmostBundleAsync_Success_SendsFrontmostCommand() {
        var runner = HappyRunner("ok frontmost bundle=com.auxbrain.egginc");
        var driver = new IosUiDriver(new FakeConnections(runner), DefaultOptions);

        var result = await driver.FrontmostBundleAsync(IosTarget, default);

        Assert.True(result.Ok);
        Assert.Equal("com.auxbrain.egginc", result.Value);
        Assert.Contains(runner.Calls,
            c => c.Exe == "ssh" && c.Args[^1].Contains("printf %s 'frontmost'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FrontmostBundleAsync_ErrLine_ReturnsError() {
        var runner = new FakeRunner((_, args) => Presence(args, present: true, doneLine: "err frontmost ?"));
        var driver = new IosUiDriver(new FakeConnections(runner), DefaultOptions);

        var result = await driver.FrontmostBundleAsync(IosTarget, default);

        Assert.Equal(DeviceOutcome.Error, result.Outcome);
        Assert.Equal("err frontmost ?", result.Note);
    }

    [Fact]
    public async Task LaunchAppAsync_IssuesUiopenCommand() {
        var runner = new FakeRunner((_, _) => new ProcessResult(0, "", ""));
        var driver = new IosUiDriver(new FakeConnections(runner), DefaultOptions);

        var result = await driver.LaunchAppAsync(IosTarget, "com.auxbrain.egginc", default);

        Assert.True(result.Ok);
        Assert.Contains(runner.Calls,
            c => c.Exe == "ssh" && c.Args[^1] == "uiopen --bundleid com.auxbrain.egginc");
    }

    private static ProcessResult Presence(string[] args, bool present, string? doneLine = "ok tap") {
        string cmd = args[^1];
        if (cmd.Contains("test -f", StringComparison.Ordinal))
            return present ? new ProcessResult(0, "tweak-present\n", "") : new ProcessResult(0, "tweak-absent\n", "");
        if (cmd.Contains("[ -f /tmp/egi-uinav.done ]", StringComparison.Ordinal))
            return doneLine is null ? new ProcessResult(1, "", "") : new ProcessResult(0, doneLine + "\n", "");
        return new ProcessResult(0, "", "");
    }

    private static FakeRunner HappyRunner(string doneLine, byte[]? jsonBytes = null, byte[]? pngBytes = null) =>
        new((exe, args) => {
            if (exe == "scp") {
                string remote = args[^2];
                if (jsonBytes is not null && remote.Contains("egi-uinav.json", StringComparison.Ordinal)) {
                    File.WriteAllBytes(args[^1], jsonBytes);
                    return new ProcessResult(0, "", "");
                }

                if (pngBytes is not null && remote.Contains("egi-uinav.png", StringComparison.Ordinal)) {
                    File.WriteAllBytes(args[^1], pngBytes);
                    return new ProcessResult(0, "", "");
                }

                return new ProcessResult(1, "", "scp: no such file");
            }

            return Presence(args, present: true, doneLine);
        });

    private sealed class FakeConnections(IProcessRunner runner, bool sshConfigured = true) : IDeviceConnectionFactory {
        public IDeviceConnection? For(DeviceTarget target) => Ios(target.Target);

        public SshDeviceConnection? Ios(string? hostFallback = null) =>
            sshConfigured ? new SshDeviceConnection(runner, new SshEndpoint("phone", "2222", "/key")) : null;
    }

    private sealed class FakeRunner(Func<string, string[], ProcessResult> fn) : IProcessRunner {
        public readonly List<(string Exe, string[] Args)> Calls = [];

        public Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct) {
            Calls.Add((exe, args));
            return Task.FromResult(fn(exe, args));
        }
    }
}
