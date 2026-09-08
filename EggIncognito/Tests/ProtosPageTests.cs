using System.Net;
using Bunit;
using EggIdentity.Contract;
using EggIncognito.Capture;
using EggIncognito.Core.Services;
using EggIncognito.Services;
using EggIncognito.Services.Routes;
using EggIncognito.Services.Workbench;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProtosPage = EggIncognito.Components.Pages.Protos;

namespace EggIncognito.Tests;

public class ProtosPageTests {
    [Collection(SharedAppCollection.Name)]
    public class Integration(SharedAppFactory f) {
        private readonly WebApplicationFactory<Program> _f = f;

        [Fact]
        public async Task Protos_Anonymous_ServesTheShell() {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/protos");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Contains("blazor.web.js", await r.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ProtoDataRoute_StillResponds() =>
            Assert.Equal(HttpStatusCode.OK, (await _f.CreateClient().GetAsync("/protodata")).StatusCode);

        [Fact]
        public async Task CaptureRoute_ServesTheShell() =>
            Assert.Equal(HttpStatusCode.OK, (await _f.CreateClient().GetAsync("/capture")).StatusCode);

        [Fact]
        public async Task CaptureDeviceRoute_ServesTheShell() =>
            Assert.Equal(HttpStatusCode.OK,
                (await _f.CreateClient().GetAsync("/capture/device/frame-iphone")).StatusCode);

        [Fact]
        public async Task SubscribeRoute_StillResponds() =>
            Assert.Equal(HttpStatusCode.OK, (await _f.CreateClient().GetAsync("/protos/subscribe")).StatusCode);
    }

    public class Component : BunitContext {
        private void Wire(UserRole role) {
            Services.AddSingleton<ICurrentUser>(new FakeUser(role));
            Services.AddSingleton(new AuthState(false));
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
            var yaml = new RouteCatalog("__no_routes_yaml__");
            Services.AddSingleton<IRouteCatalog>(yaml);
            Services.AddSingleton<IRouteCatalogReport>(
                new RouteCatalogReport(yaml, yaml, new NonBinaryRouteCatalog(yaml, null, null), null, null));
            Services.AddSingleton<IProtoReflection, ProtoReflection>();
            Services.AddSingleton<IAppMode>(new FakeAppMode());
            Services.AddSingleton<ISealedProxy>(new FakeSealedProxy());
            Services.AddSingleton<ICaptureContributionKinds>(new CaptureContributionKinds([]));
            Services.AddWorkbenchStates();
            Services.AddHttpClient();

            JSInterop.Mode = JSRuntimeMode.Loose;
        }

        [Fact]
        public void EmptyRegistry_ShowsEmptyState() {
            Wire(UserRole.Viewer);
            var cut = Render<ProtosPage>();

            Assert.Contains("No proto versions yet.", cut.Markup);
        }

        [Fact]
        public void Anonymous_RendersTableShell_NoBackfillPanel() {
            Wire(UserRole.Viewer);
            var cut = Render<ProtosPage>();

            Assert.Contains("pd-grid", cut.Markup);
            Assert.Contains("pd-band", cut.Markup);
            Assert.Contains("pd-w-support", cut.Markup);
            Assert.DoesNotContain("id=\"backfillPanel\"", cut.Markup);
        }

        [Fact]
        public void AboutWidget_CarriesTheIconRow_AndTheFloatingBubblesAreGone() {
            Wire(UserRole.Viewer);
            var cut = Render<ProtosPage>();

            Assert.Contains("pd-brand-links", cut.Markup);
            Assert.Contains("aria-label=\"EggIncognito on GitHub\"", cut.Markup);
            Assert.Contains("aria-label=\"Support the project\"", cut.Markup);
            Assert.DoesNotContain("gh-bubble", cut.Markup);
            Assert.DoesNotContain("support-bubble", cut.Markup);
        }

        [Fact]
        public void SideColumn_LeadsWithDevicesThenWorkbenches_AndHasNoDataWidget() {
            Wire(UserRole.Viewer);
            var cut = Render<ProtosPage>();

            Assert.Contains("pd-w-devices", cut.Markup);
            Assert.Contains("pd-w-workbench", cut.Markup);
            Assert.Contains(">Notifications<", cut.Markup);
            Assert.DoesNotContain("pd-w-data", cut.Markup);
            Assert.DoesNotContain("Fixtures", cut.Markup);
        }
    }

    private sealed class FakeUser(UserRole role) : ICurrentUser {
        public bool IsAuthenticated => role != UserRole.Viewer;
        public Guid? UserId => null;
        public string? DiscordId => IsAuthenticated ? "fake" : null;
        public string? Username => IsAuthenticated ? "fake" : null;
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => role;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => role >= need;
    }

    private sealed class FakeAppMode : IAppMode {
        public AppMode Mode => AppMode.Local;
        public bool CanCapture => false;
        public bool CanWrite => false;
        public bool HostedCaptureEnabled => false;
    }

    private sealed class FakeSealedProxy : ISealedProxy {
        public bool IsConfigured => false;
        public Task<bool> CanUseAsync(ICurrentUser user, CancellationToken ct = default) => Task.FromResult(false);
        public HttpClient CreateEgressClient() => new();
    }
}
