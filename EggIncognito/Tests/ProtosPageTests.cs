using System.Net;
using Bunit;
using EggIdentity.Auth;
using EggIdentity.Contract;
using EggIdentity.UI;
using EggIdentity.UI.Suite;
using EggIncognito.Capture;
using EggIncognito.Core.Services;
using EggIncognito.Models.Observations;
using EggIncognito.Services;
using EggIncognito.Services.Coverage;
using EggIncognito.Services.Devices;
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
            Services.AddEggIdentitySuite("incognito");
            Services.AddSingleton<ICurrentUser>(role != UserRole.Viewer
                ? new FakeUser(Guid.NewGuid(), role, DiscordId: "tester").Accessor()
                : new AnonymousUser());
            Services.AddSingleton(new AuthState(false));
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
            Services.AddEggIdentityDownloads();
            Services.AddEggIdentityViewerCookies(_ => { });
            var yaml = new RouteCatalog("__no_routes_yaml__");
            Services.AddSingleton<IRouteCatalog>(yaml);
            Services.AddSingleton<IRouteCatalogReport>(
                new RouteCatalogReport(yaml, yaml, new NonBinaryRouteCatalog(yaml, null, null), null, null));
            Services.AddSingleton<IProtoReflection, ProtoReflection>();
            Services.AddSingleton<IAppMode>(new FakeAppMode());
            Services.AddSingleton<ISealedProxy>(new FakeSealedProxy());
            Services.AddSingleton<ICaptureContributionKinds>(new CaptureContributionKinds([]));
            Services.AddWorkbenchStates();
            Services.AddSingleton(new DropStats(new EmptyObservations()));
            Services.AddHttpClient();

            JSInterop.Mode = JSRuntimeMode.Loose;
        }

        private sealed class EmptyObservations : IArtifactObservationQuery {
            public Task<IReadOnlyDictionary<(string Family, string Level, string Rarity), int>> ConsumedCountsAsync(CancellationToken ct) =>
                Task.FromResult<IReadOnlyDictionary<(string Family, string Level, string Rarity), int>>(
                    new Dictionary<(string Family, string Level, string Rarity), int>());

            public Task<IReadOnlyList<IReadOnlyList<ArtifactByproductRow>>> ConsumeByproductsAsync(
                string family, string level, string rarity, CancellationToken ct) =>
                Task.FromResult<IReadOnlyList<IReadOnlyList<ArtifactByproductRow>>>([]);

            public Task<IReadOnlyList<double>> ConsumeGoldenEggsAsync(string family, string level, string rarity, CancellationToken ct) =>
                Task.FromResult<IReadOnlyList<double>>([]);

            public Task<IReadOnlyList<double>> ConsumePiggyFillAsync(string family, string level, string rarity, CancellationToken ct) =>
                Task.FromResult<IReadOnlyList<double>>([]);

            public Task<IReadOnlyDictionary<(string Family, string Level, string Rarity), int>> ConsumeEventCountsAsync(CancellationToken ct) =>
                Task.FromResult<IReadOnlyDictionary<(string Family, string Level, string Rarity), int>>(
                    new Dictionary<(string Family, string Level, string Rarity), int>());

            public Task<IReadOnlyList<(string Family, string Level, string Rarity, IReadOnlyList<ArtifactByproductRow> Byproducts)>>
                ConsumesDroppingAsync(string name, CancellationToken ct) =>
                Task.FromResult<IReadOnlyList<(string Family, string Level, string Rarity, IReadOnlyList<ArtifactByproductRow> Byproducts)>>([]);
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
