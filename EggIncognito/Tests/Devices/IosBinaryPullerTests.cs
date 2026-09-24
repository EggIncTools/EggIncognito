using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class IosBinaryPullerTests {
    private const string BundleId = "com.auxbrain.egginc";
    private const string BinPath = "/private/var/containers/Bundle/Application/ABC/egginc.app/egginc";

    private static IosBinaryPuller Puller(IProcessRunner runner, string host) =>
        new(new SshDeviceConnection(runner, new SshEndpoint(host, "2222", "/key")));

    [Fact]
    public async Task Pull_LocateEmpty_ReturnsNull() {
        var runner = new FakeRunner((exe, _) => new ProcessResult(0, "", ""));
        var puller = Puller(runner, "1.2.3.4");
        Assert.Null(await puller.PullBinaryAsync(BundleId, default));
    }

    [Fact]
    public async Task Pull_ScpFails_ReturnsNull() {
        var runner = new FakeRunner((exe, _) => exe == "ssh"
            ? new ProcessResult(0, BinPath + "\n", "")
            : new ProcessResult(1, "", "scp: no such file"));
        var puller = Puller(runner, "1.2.3.4");
        Assert.Null(await puller.PullBinaryAsync(BundleId, default));
    }

    [Fact]
    public async Task Pull_Success_ReturnsBytes_AndUsesLocatedPath() {
        byte[] payload = [0xCA, 0xFE, 0xBA, 0xBE];
        var runner = new FakeRunner((exe, args) => {
            if (exe == "ssh") return new ProcessResult(0, BinPath + "\n", "");
            File.WriteAllBytes(args[^1], payload);
            return new ProcessResult(0, "", "");
        });
        var puller = Puller(runner, "phone.local");

        byte[]? bytes = await puller.PullBinaryAsync(BundleId, default);

        Assert.NotNull(bytes);
        Assert.Equal(payload, bytes);
        var scp = runner.Calls.Single(c => c.exe == "scp");
        Assert.Contains(scp.args, a => a == $"root@phone.local:{BinPath}");
        Assert.Contains(scp.args, a => a == "2222");
        Assert.Contains(scp.args, a => a == "/key");
    }

    [Fact]
    public async Task Pull_PicksFirstLocatedLine_WhenMultiple() {
        var runner = new FakeRunner((exe, args) => {
            if (exe == "ssh") return new ProcessResult(0, $"{BinPath}\n/other/path\n", "");
            File.WriteAllBytes(args[^1], [1]);
            return new ProcessResult(0, "", "");
        });
        var puller = Puller(runner, "h");
        await puller.PullBinaryAsync(BundleId, default);
        var scp = runner.Calls.Single(c => c.exe == "scp");
        Assert.Contains(scp.args, a => a == $"root@h:{BinPath}");
    }
}
