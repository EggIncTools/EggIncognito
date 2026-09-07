using System.Net;
using System.Text.Json;
using EggIdentity.Contract;
using EggIncognito.Controllers;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;
using EggIncognito.Services;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace EggIncognito.Tests.Devices;

public class DeviceBridgeControllerTests {
    private const string Secret = "s3cret";

    private static DeviceBridgeController Make(DeviceTransportConfig cfg, IServiceProvider sp,
        IProcessRunner? runner = null, UserRole role = UserRole.Viewer, string? presentedSecret = null,
        string callerIp = "127.0.0.1", HttpContext? http = null) {
        var context = http ?? new DefaultHttpContext();
        context.RequestServices = sp;
        context.Connection.RemoteIpAddress = IPAddress.Parse(callerIp);
        if (presentedSecret is not null) context.Request.Headers[BridgeRoutes.SecretHeader] = presentedSecret;

        return new DeviceBridgeController(cfg, new FakeUser(role), NullLogger<DeviceBridgeController>.Instance,
            runner ?? new RecordingRunner(), sp) {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static IServiceProvider Claims(out DeviceClaimRegistry claims) {
        claims = new DeviceClaimRegistry(TimeProvider.System);
        return new ServiceCollection().AddSingleton(claims).BuildServiceProvider();
    }

    [Fact]
    public void Gate_BridgeDisabled_404() {
        var sp = Claims(out _);
        var c = Make(new DeviceTransportConfig { BridgeEnabled = false, ApiKey = Secret }, sp,
            presentedSecret: Secret);

        Assert.IsType<NotFoundResult>(c.Release("runtime-1"));
    }

    [Fact]
    public void Gate_RemoteMode_404_EvenWithTheRightSecret() {
        var sp = Claims(out _);
        var cfg = new DeviceTransportConfig {
            BridgeEnabled = true,
            ApiKey = Secret,
            Mode = DeviceTransportMode.Remote
        };
        var c = Make(cfg, sp, presentedSecret: Secret);

        Assert.IsType<NotFoundResult>(c.Release("runtime-1"));
    }

    [Fact]
    public void Gate_CallerOutsideAllowedCidrs_403() {
        var sp = Claims(out _);
        var cfg = new DeviceTransportConfig {
            BridgeEnabled = true,
            ApiKey = Secret,
            AllowedCidrs = ["10.0.0.0/8"]
        };
        var c = Make(cfg, sp, presentedSecret: Secret, callerIp: "192.168.1.9");

        var r = Assert.IsType<ObjectResult>(c.Release("runtime-1"));
        Assert.Equal(403, r.StatusCode);
    }

    [Fact]
    public void Gate_WrongSecret_403() {
        var sp = Claims(out _);
        var c = Make(new DeviceTransportConfig { BridgeEnabled = true, ApiKey = Secret }, sp,
            presentedSecret: "nope");

        var r = Assert.IsType<ObjectResult>(c.Release("runtime-1"));
        Assert.Equal(403, r.StatusCode);
    }

    [Fact]
    public void Gate_RightSecret_Passes() {
        var sp = Claims(out var claims);
        claims.Claim("runtime-1", TimeSpan.FromMinutes(5));
        var c = Make(new DeviceTransportConfig { BridgeEnabled = true, ApiKey = Secret }, sp,
            presentedSecret: Secret);

        Assert.IsType<OkObjectResult>(c.Release("runtime-1"));
        Assert.False(claims.IsHeld("runtime-1"));
    }

    [Fact]
    public async Task Exec_ExeOutsideBridgeExecutables_400() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var http = FormRequest(new BridgeExecSpec("rm", ["-rf", "/"]), []);
        var c = Make(new DeviceTransportConfig { BridgeEnabled = true, ApiKey = Secret }, sp,
            presentedSecret: Secret, http: http);

        var r = await c.Exec(CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(r);
        Assert.Contains("BridgeExecutables", bad.Value!.ToString());
    }

    [Fact]
    public async Task Exec_SubstitutesInputAndOutputPlaceholders() {
        var sp = new ServiceCollection().BuildServiceProvider();
        string? seenInput = null;
        var runner = new RecordingRunner((_, args) => {
            seenInput = File.ReadAllText(args[1]);
            File.WriteAllBytes(args[2], "pulled"u8.ToArray());
            return new ProcessResult(0, "done", "");
        });

        var spec = new BridgeExecSpec("adb", ["push", BridgePlaceholders.In("0"), BridgePlaceholders.Out("1")],
            ["1"]);
        var http = FormRequest(spec, [("0", "hello"u8.ToArray())]);
        var c = Make(new DeviceTransportConfig { BridgeEnabled = true, ApiKey = Secret }, sp, runner,
            presentedSecret: Secret, http: http);

        var r = await c.Exec(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(r);
        var result = Assert.IsType<BridgeExecResult>(ok.Value);
        Assert.Equal("adb", runner.Exe);
        Assert.Equal("push", runner.Args[0]);
        Assert.DoesNotContain(runner.Args, a => a.StartsWith("@egi:", StringComparison.Ordinal));
        Assert.Equal("hello", seenInput);
        Assert.Equal("done", result.Stdout);
        var output = Assert.Single(result.Outputs);
        Assert.Equal("1", output.Name);
        Assert.Equal(Convert.ToBase64String("pulled"u8.ToArray()), output.Base64);
    }

    [Fact]
    public async Task Reach_ClosedPort_ReturnsNotOk() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(new DeviceTransportConfig { BridgeEnabled = true, ApiKey = Secret }, sp,
            presentedSecret: Secret);

        var r = await c.Reach("127.0.0.1", 1, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(r);
        var reach = Assert.IsType<BridgeReachResult>(ok.Value);
        Assert.False(reach.Ok);
    }

    [Fact]
    public async Task Reach_BadPort_400() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(new DeviceTransportConfig { BridgeEnabled = true, ApiKey = Secret }, sp,
            presentedSecret: Secret);

        Assert.IsType<BadRequestObjectResult>(await c.Reach("127.0.0.1", 0, CancellationToken.None));
    }

    private static DefaultHttpContext FormRequest(BridgeExecSpec spec, (string Name, byte[] Bytes)[] inputs) {
        var http = new DefaultHttpContext();
        http.Request.ContentType = "multipart/form-data; boundary=egitest";

        var fields = new Dictionary<string, StringValues> {
            [BridgeExecParts.Spec] =
                JsonSerializer.Serialize(spec, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };

        var files = new FormFileCollection();
        foreach ((string name, byte[] bytes) in inputs) {
            var stream = new MemoryStream(bytes);
            files.Add(new FormFile(stream, 0, bytes.Length, name, name) {
                Headers = new HeaderDictionary()
            });
        }

        http.Request.Form = new FormCollection(fields, files);
        return http;
    }

    private sealed class RecordingRunner(Func<string, string[], ProcessResult>? fn = null) : IProcessRunner {
        public string? Exe { get; private set; }
        public string[] Args { get; private set; } = [];

        public Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct) {
            Exe = exe;
            Args = args;
            return Task.FromResult(fn?.Invoke(exe, args) ?? new ProcessResult(0, "", ""));
        }
    }

    private sealed class FakeUser(UserRole role) : ICurrentUser {
        public bool IsAuthenticated => true;
        public Guid? UserId => null;
        public string? DiscordId => "123";
        public string? Username => "tester";
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => role;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(Role, need);
    }
}
