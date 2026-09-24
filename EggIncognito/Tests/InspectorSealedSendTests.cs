using EggIncognito.Controllers;
using EggIncognito.Models.Inspector;
using EggIncognito.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public class InspectorSealedSendTests {
    private static InspectorApiController NewController(
        IAppMode appMode, ICurrentUser user, ISealedProxy sealedProxy) {
        var c = new InspectorApiController(
            null!, null!, null!,
            appMode, user, sealedProxy, NullLogger<InspectorApiController>.Instance) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return c;
    }

    private static SendRequest SealedSend() =>
        new("https://www.auxbrain.com/ei/first_contact", "data=x", "EggIncFirstContactResponse", true);

    [Fact]
    public async Task Send_SealedRequest_NotConfigured_403() {
        var sealedProxy = new FakeSealedProxy(false, false);
        var controller = NewController(
            new FakeAppMode(AppMode.Local), new FakeUser(supporter: true), sealedProxy);

        var ex = await Assert.ThrowsAsync<ApiException>(() => controller.Send(SealedSend()));
        Assert.Equal(StatusCodes.Status403Forbidden, ex.Status);
        Assert.Equal(1, sealedProxy.CanUseCalls);
    }

    [Fact]
    public async Task Send_HostedAnonymous_403_BeforeSealedCheck() {
        var sealedProxy = new FakeSealedProxy(true, true);
        var controller = NewController(
            new FakeAppMode(AppMode.Hosted), new FakeUser(false), sealedProxy);

        var ex = await Assert.ThrowsAsync<ApiException>(() => controller.Send(SealedSend()));
        Assert.Equal(StatusCodes.Status403Forbidden, ex.Status);
        Assert.Equal(0, sealedProxy.CanUseCalls);
    }

    private sealed class FakeAppMode(AppMode mode) : IAppMode {
        public AppMode Mode => mode;
        public bool CanCapture => false;
        public bool CanWrite => false;
        public bool HostedCaptureEnabled => false;
    }

    private sealed class FakeSealedProxy(bool configured, bool canUse) : ISealedProxy {
        public int CanUseCalls { get; private set; }
        public bool IsConfigured => configured;

        public Task<bool> CanUseAsync(ICurrentUser user, CancellationToken ct = default) {
            CanUseCalls++;
            return Task.FromResult(canUse);
        }

        public HttpClient CreateEgressClient() => new();
    }
}
