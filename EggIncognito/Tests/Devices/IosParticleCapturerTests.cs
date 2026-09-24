using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class IosParticleCapturerTests {
    private const string ScriptBody = "// fake hook";

    private static IosParticleCapturer Cap(IProcessRunner runner, string host, string? addrOffset = null) =>
        new(new SshDeviceConnection(runner, new SshEndpoint(host, "2222", "/key")), ScriptBody, addrOffset);

    [Fact]
    public async Task Capture_PushFails_ReturnsNull() {
        var runner = new FakeRunner((exe, _) => exe == "scp"
            ? new ProcessResult(1, "", "scp: permission denied")
            : new ProcessResult(0, "", ""));
        Assert.Null(await Cap(runner, "h").CaptureAsync(default));
    }

    [Fact]
    public async Task Capture_NoLogPulled_ReturnsNotOkWithFridaDiag() {
        int scpCount = 0;
        var runner = new FakeRunner((exe, args) => {
            if (exe == "scp") {
                scpCount++;
                return scpCount == 1 ? new ProcessResult(0, "", "") : new ProcessResult(1, "", "scp: no such file");
            }

            return new ProcessResult(0, "addParticle symbol not resolved", "");
        });
        var m = await Cap(runner, "h").CaptureAsync(default);
        Assert.NotNull(m);
        Assert.False(m.Value.Ok);
        Assert.Contains("symbol not resolved", m.Value.Diagnostics);
    }

    [Fact]
    public async Task Capture_InjectsAddrOffsetIntoStagedScript() {
        string? pushedContent = null;
        int scpCount = 0;
        var runner = new FakeRunner((exe, args) => {
            if (exe == "scp") {
                scpCount++;
                if (scpCount == 1) pushedContent = File.ReadAllText(args[^2]);
                return new ProcessResult(0, "", "");
            }

            return new ProcessResult(0, "", "");
        });
        await Cap(runner, "h", "0x1234abc").CaptureAsync(default);

        Assert.NotNull(pushedContent);
        Assert.Contains("const addrOffset = '0x1234abc';", pushedContent);
        Assert.Contains(ScriptBody, pushedContent);
    }

    [Fact]
    public async Task Capture_Success_PullsAndParses() {
        const string ndjson = "{\"t\":0,\"mesh\":\"0xA\",\"x\":[1,0,0,0,1,0,0,0,1,4,5,6],\"s\":0.5}";
        int scpCount = 0;
        var runner = new FakeRunner((exe, args) => {
            if (exe == "scp") {
                scpCount++;
                if (scpCount == 2) File.WriteAllText(args[^1], ndjson);
                return new ProcessResult(0, "", "");
            }

            return new ProcessResult(0, "__frida_exit_0", "");
        });
        var m = await Cap(runner, "phone").CaptureAsync(default);

        Assert.NotNull(m);
        Assert.True(m.Value.Ok);
        Assert.Equal(1, m.Value.TotalSamples);
        Assert.Equal("0xA", m.Value.Dominant!.Value.Mesh);
        Assert.Equal(4f, m.Value.Dominant!.Value.Centroid[0], 3);
        var push = runner.Calls.First(c => c.exe == "scp");
        Assert.Contains(push.args, a => a.StartsWith("root@phone:"));
        Assert.EndsWith(".js", push.args[^2]);
    }
}
