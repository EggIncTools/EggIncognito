using System.Net;
using System.Text;
using EggIdentity.Contract;
using EggIncognito.Capture;
using EggIncognito.Controllers;
using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public sealed class CaptureCaNotifierTests : IDisposable {
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private CaptureSession FreshCaSession() {
        string tmp = _tmp.CreateSubdir();
        var opts = new CaptureSessionOptions(19090, null, null,
            false, false, tmp,
            Path.Combine(tmp, "ca.cer"), false);
        return new CaptureSession(CaptureSessionManagerTests.RealContentRoot(), opts, _ => new FreshCaProxy());
    }

    private CaptureSessionManager FreshCaManager() =>
        new(HostedCaptureOptions.Defaults(), (_, _, _) => FreshCaSession());

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public async Task Start_FreshCa_NoStore_Still200_AndFlagsCaDmFailed() {
        var manager = FreshCaManager();
        var notifier = new FailingNotifier();
        var controller = new CaptureController(
            manager, new FakeAppMode(false, true),
            new FakeUser(true, true),
            HostedCaptureOptions.Defaults(), NullLogger<CaptureController>.Instance, new StubServices(notifier));

        var r = await controller.Start(CancellationToken.None);

        Assert.Equal(200, ((IStatusCodeActionResult)r).StatusCode);
        var session = manager.Get("tester");
        Assert.NotNull(session);
        Assert.True(session.CaDmFailed);
        Assert.True(session.Status.CaDmFailed);
        await session.StopAsync();
    }

    [Fact]
    public async Task DiscordNotifier_FailingHttp_ReturnsFalse() {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var notifier = new DiscordCaptureCaNotifier(
            new StubHttpFactory(handler),
            Config(new Dictionary<string, string?> { ["Discord:BotToken"] = "token" }),
            NullLogger<DiscordCaptureCaNotifier>.Instance);

        bool ok = await notifier.SendSetupAsync(Dm(), CancellationToken.None);
        Assert.False(ok);
    }

    [Fact]
    public async Task DiscordNotifier_NoToken_ReturnsFalse() {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var notifier = new DiscordCaptureCaNotifier(
            new StubHttpFactory(handler),
            Config([]),
            NullLogger<DiscordCaptureCaNotifier>.Instance);

        bool ok = await notifier.SendSetupAsync(Dm(), CancellationToken.None);
        Assert.False(ok);
    }

    private static CaptureSetupDm Dm() =>
        new("123", [0x30, 0x82, 0x01], "[2a01:4f8:c012:e15b::5]", 8443);

    [Fact]
    public void BuildMessage_ShowsProxyAddress_NoCredentials() {
        string msg = DiscordCaptureCaNotifier.BuildMessage(
            new CaptureSetupDm("123", [0x30, 0x82, 0x01], "2a01:4f8:c012:e15b::5", 8443));

        Assert.Contains("2a01:4f8:c012:e15b::5", msg);
        Assert.Contains("8443", msg);
        Assert.Contains("Auth off", msg);
        Assert.DoesNotContain("Token", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Username", msg);
        Assert.DoesNotContain("password", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MobileConfig_EmbedsCert_AndIsStablePerUser() {
        byte[] cer = [0x30, 0x82, 0x01, 0x02, 0x03];
        string a = Encoding.UTF8.GetString(MobileConfig.BuildCaProfile(cer, "user-1"));
        string b = Encoding.UTF8.GetString(MobileConfig.BuildCaProfile(cer, "user-1"));
        Assert.Equal(a, b);
        Assert.Contains("com.apple.security.root", a);
        Assert.Contains(Convert.ToBase64String(cer), a);
    }

    [Fact]
    public void MobileConfig_SameUser_DifferentCert_KeepsProfileIdentity() {
        string newCert = Convert.ToBase64String([0x30, 0x99]);
        string a = Encoding.UTF8.GetString(MobileConfig.BuildCaProfile([0x30, 0x82, 0x01], "user-1"));
        string b = Encoding.UTF8.GetString(MobileConfig.BuildCaProfile([0x30, 0x99], "user-1"));
        Assert.Equal(ProfileUuid(a), ProfileUuid(b));
        Assert.Contains(newCert, b);
    }

    [Fact]
    public void MobileConfig_DifferentUsers_GetDistinctProfiles() {
        byte[] cer = [0x30, 0x82, 0x01];
        string a = Encoding.UTF8.GetString(MobileConfig.BuildCaProfile(cer, "user-1"));
        string b = Encoding.UTF8.GetString(MobileConfig.BuildCaProfile(cer, "user-2"));
        Assert.NotEqual(ProfileUuid(a), ProfileUuid(b));
    }

    private static string ProfileUuid(string plist) =>
        plist.Split("<key>PayloadUUID</key>")[^1].Split("<string>")[1].Split("</string>")[0];

    private sealed class FailingNotifier : ICaptureCaNotifier {
        public int Calls { get; private set; }

        public Task<bool> SendSetupAsync(CaptureSetupDm dm, CancellationToken ct) {
            Calls++;
            return Task.FromResult(false);
        }
    }

    private sealed class StubServices(ICaptureCaNotifier notifier) : IServiceProvider {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ICaptureCaNotifier) ? notifier : null;
    }

    private sealed class FakeAppMode(bool canCapture, bool hostedEnabled) : IAppMode {
        public AppMode Mode => AppMode.Hosted;
        public bool CanCapture => canCapture;
        public bool CanWrite => false;
        public bool HostedCaptureEnabled => hostedEnabled;
    }

    private sealed class FakeUser(bool authed, bool supporter) : ICurrentUser {
        public bool IsAuthenticated => authed;
        public Guid? UserId => authed ? Guid.Parse("00000000-0000-0000-0000-000000000001") : null;
        public string? DiscordId => authed ? "tester" : null;
        public string? Username => authed ? "tester" : null;
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => UserRole.Viewer;
        public bool IsSupporter => supporter;
        public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(UserRole.Viewer, need);
    }

    private sealed class FreshCaProxy : ICaptureProxy {
        public bool Verbose { get; set; }
        public bool FreshCa => true;
        public string? RootThumbprint => "FRESH-THUMB";

        public async Task StartAsync(int port, string caPath, CancellationToken ct) {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(caPath))!);
            await File.WriteAllBytesAsync(caPath, [0x30, 0x82, 0x01], ct);
        }

        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
#pragma warning disable CS0067
        public event Action<CapturedFlow>? FlowCaptured;
        public event Action<int, string?>? ClientConnected;
        public event Action<int, string?>? ClientDisconnected;
        public event Action? AuxbrainConnect;
        public event Action<string>? DecryptError;
        public event Action? TrustRestored;
        public event Action<string, bool>? ConnectSeen;
        public event Action<string>? Trace;
#pragma warning restore CS0067
    }
}
