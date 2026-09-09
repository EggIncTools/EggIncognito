using System.Net;
using Bunit;
using EggIdentity.Contract;
using EggIdentity.UI;
using EggIncognito.Capture;
using EggIncognito.Components.Api;
using EggIncognito.Controllers;
using EggIncognito.Core.Services;
using EggIncognito.Models.Capture;
using EggIncognito.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public class HostedCapturePageTests {
    private static CaptureSessionManager NewManager(TempDir tmp) =>
        new(HostedCaptureOptions.Defaults(),
            (key, basePort, tier) => CaptureSessionManagerTests.NewSession(tmp,
                key == CaptureSessionManager.LocalKey ? 18080 : basePort, tier: tier));

    private sealed class FakeAppMode(bool canCapture, bool hostedEnabled) : IAppMode {
        public AppMode Mode => AppMode.Hosted;
        public bool CanCapture => canCapture;
        public bool CanWrite => false;
        public bool HostedCaptureEnabled => hostedEnabled;
    }

    private sealed class FakeUser(bool authed, bool supporter, UserRole role = UserRole.Viewer) : ICurrentUser {
        public bool IsAuthenticated => authed;
        public Guid? UserId => authed ? Guid.Parse("00000000-0000-0000-0000-000000000001") : null;
        public string? DiscordId => authed ? "tester" : null;
        public string? Username => authed ? "tester" : null;
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => role;
        public bool IsSupporter => supporter;
        public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(role, need);
    }

    private sealed class EmptyServices : IServiceProvider {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class FakeRoutes : IRouteCatalog {
        public IReadOnlyList<RouteInfo> All() => [];
        public RouteInfo? Resolve(string path) => new(path, null, "PeriodicalsResponse", false, false, null, false, false);
    }

    [Collection(HostedAppCollection.Name)]
    public class HostedDefault(HostedAppFactory f) {
        [Fact]
        public async Task Mode_Hosted_Default_ReportsHostedCaptureFalse() {
            string json = await f.CreateClient().GetStringAsync("/api/app/mode");
            Assert.Contains("\"hostedCapture\":false", json);
        }
    }

    [Collection(HostedCaptureAppCollection.Name)]
    public class CaptureEnabled(HostedCaptureAppFactory f) {
        [Fact]
        public async Task Mode_Hosted_Enabled_ReportsHostedCaptureTrue() {
            string json = await f.CreateClient().GetStringAsync("/api/app/mode");
            Assert.Contains("\"hostedCapture\":true", json);
        }

        [Fact]
        public async Task CaptureStart_HostedEnabled_Anonymous_Is401() {
            var r = await f.CreateClient().PostAsync("/api/capture/start", null);
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }
    }

    public class Component : BunitContext {
        private void Wire(TempDir tmp, bool authed, bool supporter, bool canCapture = false) {
            JSInterop.Mode = JSRuntimeMode.Loose;
            Services.AddSingleton<IAppMode>(new FakeAppMode(canCapture, true));
            Services.AddSingleton<ICurrentUser>(new FakeUser(authed, supporter));
            Services.AddSingleton(HostedCaptureOptions.Defaults());
            Services.AddSingleton(NewManager(tmp));
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
            Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            Services.AddSingleton(new AuthState(false));
            Services.AddSingleton<ICaptureContributionKinds>(new CaptureContributionKinds([]));
            Services.AddEggIdentityToasts();
            Services.AddHttpClient();
        }

        [Fact]
        public void Anonymous_ShowsLoginPrompt() {
            using var tmp = new TempDir();
            Wire(tmp, false, false);
            var cut = Render<CapturePane>();
            Assert.NotNull(cut.Find("#hostedLogin"));
            Assert.Empty(cut.FindAll("#hostedSetupCard"));
        }

        [Fact]
        public void NonSupporter_ShowsSetupCard_AndDashboard() {
            using var tmp = new TempDir();
            Wire(tmp, true, false);
            var cut = Render<CapturePane>();
            Assert.NotNull(cut.WaitForElement("#hostedSetupCard"));
            Assert.NotNull(cut.Find("#flowsPanel"));
            Assert.NotNull(cut.Find("#limitedNotice"));
        }

        [Fact]
        public void Supporter_ShowsSetupCard_AndDashboard() {
            using var tmp = new TempDir();
            Wire(tmp, true, true);
            var cut = Render<CapturePane>();
            Assert.NotNull(cut.WaitForElement("#hostedSetupCard"));
            Assert.NotNull(cut.Find("#flowsPanel"));
            Assert.Empty(cut.FindAll("#limitedNotice"));
            Assert.Empty(cut.FindAll("#statsPanel"));
            Assert.Empty(cut.FindAll(".wbx-kpi"));
        }

        [Fact]
        public void SetupCard_StartsExpanded_WithInlineStepActions() {
            using var tmp = new TempDir();
            Wire(tmp, true, true);
            var cut = Render<CapturePane>();
            cut.WaitForElement("#hostedSetupCard");
            string markup = cut.Markup;

            Assert.DoesNotContain("collapsed", cut.Find("#hostedSetupCard .insp-disc").ClassName);
            Assert.NotEmpty(cut.FindAll("#hostedSetupCard .insp-disc-toggle .ep-caret"));
            Assert.Contains("New address", markup);
            Assert.Contains("Re-send setup DM", markup);
            Assert.DoesNotContain("Hide steps", markup);
            Assert.DoesNotContain("this machine", markup);
            Assert.DoesNotContain("your machine", markup);
        }

        [Fact]
        public void RequestsHead_HidesClearAndExport_WhenNoFlows() {
            using var tmp = new TempDir();
            Wire(tmp, true, true);
            var cut = Render<CapturePane>();
            cut.WaitForElement("#flowsPanel");
            string markup = cut.Markup;

            Assert.DoesNotContain("Export HAR", markup);
            Assert.Empty(cut.FindAll("#flowsPanel .flow-tools .btn-danger"));
            Assert.Contains("Start capture", markup);
            Assert.NotEmpty(cut.FindAll("#flowsPanel .flow-filters"));
        }

        [Fact]
        public void LocalCapture_ShowsDashboardWithoutSetupCard() {
            using var tmp = new TempDir();
            Wire(tmp, false, false, canCapture: true);
            var cut = Render<CapturePane>();
            Assert.NotNull(cut.WaitForElement("#localSetupCard"));
            Assert.NotNull(cut.Find("#flowsPanel"));
            Assert.NotNull(cut.Find("#detailPanel"));
            Assert.Empty(cut.FindAll("#hostedSetupCard"));
            Assert.DoesNotContain("this machine", cut.Markup);
        }

        [Fact]
        public void Supporter_SetupCard_ShowsProxyAddress_NoAuthCredentials() {
            using var tmp = new TempDir();
            Wire(tmp, true, true);
            var cut = Render<CapturePane>();
            cut.WaitForElement("#proxyHost");
            string markup = cut.Markup;

            Assert.NotNull(cut.Find("#proxyHost"));
            Assert.Contains("Server", markup);
            Assert.Contains("Port", markup);
            Assert.Contains("Auth", markup);

            Assert.Empty(cut.FindAll("#proxyProfileCard"));
            Assert.Empty(cut.FindAll("#mintedToken"));
            Assert.DoesNotContain("Username", markup);
            Assert.DoesNotContain("Token", markup);
            Assert.DoesNotContain("Cycle token", markup);
            Assert.DoesNotContain("SSID", markup);
            Assert.DoesNotContain("DM proxy profile", markup);
        }
    }

    public sealed class StartGate : IDisposable {
        private readonly TempDir _tmp = new();

        public void Dispose() => _tmp.Dispose();

        private static CaptureController Controller(CaptureSessionManager manager, ICurrentUser user) =>
            new(manager, new FakeAppMode(false, true), user,
                HostedCaptureOptions.Defaults(), NullLogger<CaptureController>.Instance, new EmptyServices());

        [Fact]
        public async Task Start_Anonymous_Is401() {
            var r = await Controller(NewManager(_tmp), new FakeUser(false, false)).Start(CancellationToken.None);
            Assert.Equal(401, ((IStatusCodeActionResult)r).StatusCode);
        }

        [Fact]
        public async Task Start_NonSupporter_StartsLimitedSession() {
            var manager = NewManager(_tmp);
            var r = await Controller(manager, new FakeUser(true, false)).Start(CancellationToken.None);
            Assert.Equal(200, ((IStatusCodeActionResult)r).StatusCode);
            var session = manager.Get("tester");
            Assert.NotNull(session);
            Assert.Equal(CaptureState.Running, session.State);
            await session.StopAsync();
        }

        [Fact]
        public async Task Start_Supporter_StartsOwnSession() {
            var manager = NewManager(_tmp);
            var r = await Controller(manager, new FakeUser(true, true)).Start(CancellationToken.None);
            Assert.Equal(200, ((IStatusCodeActionResult)r).StatusCode);
            var session = manager.Get("tester");
            Assert.NotNull(session);
            Assert.Equal(CaptureState.Running, session.State);
            await session.StopAsync();
        }

        [Fact]
        public async Task ProxyAddress_NonSupporter_ReachesDbCheck() {
            var r = await Controller(NewManager(_tmp), new FakeUser(true, false))
                .ProxyAddress(CancellationToken.None);
            Assert.Equal(503, ((IStatusCodeActionResult)r).StatusCode);
        }
    }

    public sealed class SaveGate : IDisposable {
        private readonly TempDir _tmp = new();

        public void Dispose() => _tmp.Dispose();

        private (CaptureController Controller, CaptureSession Session) WithFlowSession(ICurrentUser user) {
            var manager = NewManager(_tmp);
            var session = manager.GetOrCreate("tester");
            var controller = new CaptureController(
                manager, new FakeAppMode(false, true), user,
                HostedCaptureOptions.Defaults(), NullLogger<CaptureController>.Instance, new EmptyServices());
            return (controller, session);
        }

        private static long PublishFlow(CaptureSession session) {
            var stored = session.Hub.Publish(
                new DashboardFlow(0, "", "ei/first_contact", "POST", 200, null, null, "AA==", null),
                "12:00:00");
            return stored!.Id;
        }

        [Fact]
        public async Task Save_Hosted_ViewerNonSupporter_Is403() {
            var (controller, session) = WithFlowSession(new FakeUser(true, false));
            long id = PublishFlow(session);
            var r = await controller.SaveEndpoint(new SaveFlowRequest(id), new FakeRoutes());
            Assert.Equal(403, ((IStatusCodeActionResult)r).StatusCode);
        }

        [Fact]
        public async Task Save_Hosted_Supporter_PassesGate_Then503NoDb() {
            var (controller, session) = WithFlowSession(new FakeUser(true, true));
            long id = PublishFlow(session);
            var r = await controller.SaveEndpoint(new SaveFlowRequest(id), new FakeRoutes());
            Assert.Equal(503, ((IStatusCodeActionResult)r).StatusCode);
        }

        [Fact]
        public async Task Save_Hosted_Contributor_PassesGate_Then503NoDb() {
            var (controller, session) = WithFlowSession(new FakeUser(true, false, UserRole.Contributor));
            long id = PublishFlow(session);
            var r = await controller.SaveEndpoint(new SaveFlowRequest(id), new FakeRoutes());
            Assert.Equal(503, ((IStatusCodeActionResult)r).StatusCode);
        }
    }
}
