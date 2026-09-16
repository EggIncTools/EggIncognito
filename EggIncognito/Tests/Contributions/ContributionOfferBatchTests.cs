using EggIdentity.Contract;
using EggIncognito.Capture;
using EggIncognito.Controllers;
using EggIncognito.Data.Services;
using EggIncognito.Models.Contributions;
using EggIncognito.Services;
using EggIncognito.Services.Contributions;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests.Contributions;

public class ContributionOfferBatchTests {
    private const string Craft = "ei_afx/craft_artifact";

    [Fact]
    public void OfferBatch_RecordsContributableFlowsAndReportsTheRest() {
        var hub = new CaptureHub();
        long craftId = hub.Publish(Flow(Craft), "12:00:00")!.Id;
        long otherId = hub.Publish(Flow("ei/first_contact"), "12:00:01")!.Id;

        var result = Controller(hub).OfferBatch(
            new ContributionOfferBatchRequest("dev", [craftId, craftId, otherId, 9999]));

        var accepted = Assert.IsType<AcceptedResult>(result);
        var body = Assert.IsType<ContributionOfferBatchResult>(accepted.Value);
        Assert.Equal(1, body.Recorded);
        Assert.Equal(new long[] { otherId, 9999 }, body.Missing);
    }

    [Fact]
    public void OfferBatch_RejectsEmptyAndOversizedIdLists() {
        var controller = Controller(new CaptureHub());

        Assert.IsType<BadRequestObjectResult>(
            controller.OfferBatch(new ContributionOfferBatchRequest("dev", [])));
        Assert.IsType<BadRequestObjectResult>(
            controller.OfferBatch(new ContributionOfferBatchRequest("dev", [.. Enumerable.Range(1, 5001).Select(i => (long)i)])));
    }

    private static ContributionsController Controller(CaptureHub hub) {
        var kinds = new CaptureContributionKinds([new ArtifactContributionKind()]);
        var options = ContributionOptions.Defaults();
        var db = new EggIncognitoDbContext(new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none").Options);
        var services = new ServiceCollection();
        services.AddSingleton(new ContributionStore(db));
        services.AddSingleton<IDeviceCaptureHubs>(new OneHub(hub));
        services.AddSingleton(new ContributionRecorder(
            new NoScopes(), kinds, options, NullLogger<ContributionRecorder>.Instance));
        return new ContributionsController(new FakeUser(), kinds, options,
            services.BuildServiceProvider(), NullLogger<ContributionsController>.Instance);
    }

    private static DashboardFlow Flow(string path) =>
        new(0, "", path, "POST", 200, null, null, "", null,
            RequestJsonRaw: "{}", ResponseJsonRaw: "{}");

    private sealed class OneHub(CaptureHub hub) : IDeviceCaptureHubs {
        public CaptureHub? HubFor(string deviceId) => deviceId == "dev" ? hub : null;
    }

    private sealed class NoScopes : IServiceScopeFactory {
        public IServiceScope CreateScope() => throw new NotSupportedException();
    }

    private sealed class FakeUser : ICurrentUser {
        public bool IsAuthenticated => true;
        public Guid? UserId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string? DiscordId => "1";
        public string? Username => "tester";
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => UserRole.Admin;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => true;
    }
}
