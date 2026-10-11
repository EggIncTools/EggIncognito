using EggIdentity.Contract;
using EggIncognito.Capture;
using EggIncognito.Controllers;
using EggIncognito.Models.Contributions;
using EggIncognito.Services.Contributions;
using EggIncognito.Services.Devices;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests.Contributions;

public class ContributionOfferBatchTests {
    private const string Craft = "ei_afx/craft_artifact";

    private sealed class CraftKind : ICaptureContributionKind {
        public string Kind => "test-craft";
        public IReadOnlyCollection<string> Routes => [Craft];

        public ContributionDraft? Build(DashboardFlow flow) =>
            flow.Path == Craft ? new ContributionDraft(Kind, "craft", "{}", flow.Id.ToString(), null) : null;
    }

    [Fact]
    public void OfferBatch_RecordsContributableFlowsAndReportsTheRest() {
        var hub = new CaptureHub();
        long craftId = hub.Publish(Flow(Craft), "12:00:00")!.Id;
        long otherId = hub.Publish(Flow("ei/first_contact"), "12:00:01")!.Id;

        var (controller, recorder, hubs) = Setup(hub);
        var result = controller.OfferBatch(
            new ContributionOfferBatchRequest("dev", [craftId, craftId, otherId, 9999]), recorder, hubs);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var body = Assert.IsType<ContributionOfferBatchResult>(accepted.Value);
        Assert.Equal(1, body.Recorded);
        Assert.Equal(new long[] { otherId, 9999 }, body.Missing);
    }

    [Fact]
    public void OfferBatch_RejectsEmptyAndOversizedIdLists() {
        var (controller, recorder, hubs) = Setup(new CaptureHub());

        Assert.Equal(400, Assert.IsType<ObjectResult>(
            controller.OfferBatch(new ContributionOfferBatchRequest("dev", []), recorder, hubs)).StatusCode);
        Assert.Equal(400, Assert.IsType<ObjectResult>(
            controller.OfferBatch(
                new ContributionOfferBatchRequest("dev", [.. Enumerable.Range(1, 5001).Select(i => (long)i)]),
                recorder, hubs)).StatusCode);
    }

    private static (ContributionsController Controller, ContributionRecorder Recorder, IDeviceCaptureHubs Hubs)
        Setup(CaptureHub hub) {
        var kinds = new CaptureContributionKinds([new CraftKind()]);
        var options = ContributionOptions.Defaults();
        var recorder = new ContributionRecorder(
            new NoScopes(), kinds, options, NullLogger<ContributionRecorder>.Instance, TimeProvider.System);
        var controller = new ContributionsController(
            new FakeUser(Guid.Parse("11111111-1111-1111-1111-111111111111"), UserRole.Admin, DiscordId: "1").Accessor(),
            kinds, options);
        return (controller, recorder, new OneHub(hub));
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
}
