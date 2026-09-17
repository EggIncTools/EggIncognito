using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class DeviceProbeTests {
    private const string Plist = """
                                 <?xml version="1.0"?><plist version="1.0"><array>
                                 <dict><key>CFBundleIdentifier</key><string>com.auxbrain.egginc</string>
                                 <key>CFBundleShortVersionString</key><string>1.35.8</string></dict>
                                 </array></plist>
                                 """;

    [Fact]
    public async Task Adb_Reachable_ReturnsVersionAndBuild() {
        var runner = new FakeRunner((exe, args) => {
            Assert.Equal("adb", exe);
            Assert.Contains("RF8X20GLYDY", args);
            Assert.Contains("com.auxbrain.egginc", args);
            return new ProcessResult(0, "versionCode=111344 minSdk=24\nversionName=1.35.7\n", "");
        });
        var probe = new AdbDeviceProbe(runner, "RF8X20GLYDY", "com.auxbrain.egginc");
        var r = await probe.ProbeAsync(default);
        Assert.True(r.Reachable);
        Assert.Equal("1.35.7", r.InstalledAppVersion);
        Assert.Equal("111344", r.InstalledBuild);
    }

    [Fact]
    public async Task Adb_NoDevice_NotReachable() {
        var runner = new FakeRunner((_, _) => new ProcessResult(1, "", "error: device 'RF8X20GLYDY' not found"));
        var probe = new AdbDeviceProbe(runner, "RF8X20GLYDY", "com.auxbrain.egginc");
        var r = await probe.ProbeAsync(default);
        Assert.False(r.Reachable);
        Assert.NotNull(r.Note);
    }

    [Fact]
    public async Task Ios_Reachable_ReturnsAppVersionNullBuild() {
        var runner = new FakeRunner((exe, args) => {
            Assert.Equal("ideviceinstaller", exe);
            Assert.Contains("3489c6b0", args);
            Assert.Contains("-l", args);
            return new ProcessResult(0, Plist, "");
        });
        var probe = new IosDeviceProbe(runner, "3489c6b0", "com.auxbrain.egginc");
        var r = await probe.ProbeAsync(default);
        Assert.True(r.Reachable);
        Assert.Equal("1.35.8", r.InstalledAppVersion);
        Assert.Null(r.InstalledBuild);
    }

    [Fact]
    public async Task Ios_CsvFallback_AlsoParses() {
        const string csv = "com.auxbrain.egginc, \"1.35.8\", \"Egg, Inc.\"\n";
        var runner = new FakeRunner((_, _) => new ProcessResult(0, csv, ""));
        var probe = new IosDeviceProbe(runner, "3489c6b0", "com.auxbrain.egginc");
        var r = await probe.ProbeAsync(default);
        Assert.Equal("1.35.8", r.InstalledAppVersion);
    }

    [Fact]
    public async Task Ios_AppNotInstalled_ReachableButNoVersion() {
        const string empty = """<?xml version="1.0"?><plist version="1.0"><array></array></plist>""";
        var runner = new FakeRunner((_, _) => new ProcessResult(0, empty, ""));
        var probe = new IosDeviceProbe(runner, "3489c6b0", "com.auxbrain.egginc");
        var r = await probe.ProbeAsync(default);
        Assert.True(r.Reachable);
        Assert.Null(r.InstalledAppVersion);
        Assert.NotNull(r.Note);
    }

    [Fact]
    public async Task Ios_ToolMissing_NotReachable() {
        var runner = new FakeRunner((_, _) => new ProcessResult(-1, "", "ideviceinstaller: not found"));
        var probe = new IosDeviceProbe(runner, "3489c6b0", "com.auxbrain.egginc");
        var r = await probe.ProbeAsync(default);
        Assert.False(r.Reachable);
    }

    private static IosSshVersionProbe SshProbe(IProcessRunner runner) =>
        new(new SshDeviceConnection(runner, new SshEndpoint("192.168.1.175", "2222", "/k")),
            "com.auxbrain.egginc");

    [Fact]
    public async Task IosSsh_ReadsVersionAndBuildFromPlutil() {
        var runner = new FakeRunner((exe, args) => {
            Assert.Equal("ssh", exe);
            Assert.Contains("CFBundleShortVersionString", args[^1]);
            Assert.Contains("CFBundleVersion", args[^1]);
            return new ProcessResult(0, "1.37.2\n1.37.2.1\n", "");
        });
        var r = await SshProbe(runner).ProbeAsync(default);
        Assert.True(r.Reachable);
        Assert.Equal("1.37.2", r.InstalledAppVersion);
        Assert.Equal("1.37.2.1", r.InstalledBuild);
        Assert.NotNull(r.Note);
    }

    [Fact]
    public async Task IosSsh_AppDirMissing_ReachableButNoVersion() {
        var runner = new FakeRunner((_, _) => new ProcessResult(3, "", ""));
        var r = await SshProbe(runner).ProbeAsync(default);
        Assert.True(r.Reachable);
        Assert.Null(r.InstalledAppVersion);
    }

    [Fact]
    public async Task IosSsh_SshFails_NotReachable() {
        var runner = new FakeRunner((_, _) => new ProcessResult(255, "", "connection refused"));
        var r = await SshProbe(runner).ProbeAsync(default);
        Assert.False(r.Reachable);
    }

    [Fact]
    public async Task IosSsh_BuildAbsent_StillReturnsAppVersion() {
        var runner = new FakeRunner((_, _) => new ProcessResult(0, "1.37.2\n\n", ""));
        var r = await SshProbe(runner).ProbeAsync(default);
        Assert.Equal("1.37.2", r.InstalledAppVersion);
        Assert.Null(r.InstalledBuild);
    }

    private sealed class FakeRunner(Func<string, string[], ProcessResult> fn) : IProcessRunner {
        public Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct) =>
            Task.FromResult(fn(exe, args));
    }
}
