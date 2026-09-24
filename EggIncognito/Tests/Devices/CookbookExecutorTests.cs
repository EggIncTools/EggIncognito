using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices.Cookbooks;

namespace EggIncognito.Tests.Devices;

public class CookbookExecutorTests {
    private static readonly DeviceTarget Target = new("dev", Platforms.Android, "emulator-5554", "com.auxbrain.egg");

    private sealed class FixedStep(string id, string title, CookbookStepStatus status, string? note,
        bool echoNote = false) : CookbookStep {
        public override string Id => id;
        public override string Title => title;

        public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
            var lines = new List<string> { $"{id} ran" };
            if (echoNote && note is not null) {
                lines.Add(note);
                await context.Progress(note);
            }

            return new CookbookStepResult(id, title, status, note, lines);
        }
    }

    private sealed class Plan(string id, string title, params CookbookStep[] steps) : IStepCookbook {
        public string Id => id;
        public string Title => title;
        public string Summary => "";

        public Task<DeviceCookbookInfo> DescribeAsync(DeviceTarget target, CancellationToken ct) =>
            Task.FromResult(new DeviceCookbookInfo(Id, Title, Summary, true));

        public Task<IReadOnlyList<CookbookStep>> PlanAsync(DeviceTarget target, string? argument,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CookbookStep>>(steps);

        public Task<DeviceCookbookRun> RunAsync(DeviceCookbookContext context, CancellationToken ct) =>
            CookbookExecutor.RunStepsAsync(this, context, ct);
    }

    private static DeviceCookbookContext Context(List<string> progress) =>
        new(Target, null, line => {
            progress.Add(line);
            return Task.CompletedTask;
        });

    [Fact]
    public async Task FailedRunCarriesTheStepNoteVerbatimAndTheStepIdentity() {
        const string note = "install-multiple failed: adb: failed to finalize session";
        var cookbook = new Plan("install-app", "Install app",
            new FixedStep("install-app", "Install app", CookbookStepStatus.Failed, note));
        var progress = new List<string>();

        var run = await CookbookExecutor.RunStepsAsync(cookbook, Context(progress), CancellationToken.None);

        Assert.False(run.Ok);
        Assert.Equal(note, run.Note);
        Assert.Equal("install-app", run.FailedStep);
        Assert.Equal("Install app", run.FailedStepTitle);
        Assert.Equal("Install app: " + note, run.Failure);
    }

    [Fact]
    public async Task ExecutorLogsTheFailureNoteExactlyOnce() {
        const string note = "boom";
        var cookbook = new Plan("x", "X", new FixedStep("s", "S", CookbookStepStatus.Failed, note));
        var progress = new List<string>();

        var run = await CookbookExecutor.RunStepsAsync(cookbook, Context(progress), CancellationToken.None);

        Assert.Equal(1, run.Log.Count(l => l == note));
        Assert.Equal(1, progress.Count(l => l == note));
    }

    [Fact]
    public async Task ExecutorDoesNotRepeatANoteTheStepAlreadyLogged() {
        const string note = "boom";
        var cookbook = new Plan("x", "X", new FixedStep("s", "S", CookbookStepStatus.Failed, note, echoNote: true));
        var progress = new List<string>();

        var run = await CookbookExecutor.RunStepsAsync(cookbook, Context(progress), CancellationToken.None);

        Assert.Equal(1, run.Log.Count(l => l == note));
        Assert.Equal(1, progress.Count(l => l == note));
    }

    [Fact]
    public async Task SuccessfulMultiStepRunSummarizesWithTheCookbookTitle() {
        var cookbook = new Plan("bring-up", "Bring up",
            new FixedStep("a", "A", CookbookStepStatus.Ok, "a done"),
            new FixedStep("b", "B", CookbookStepStatus.Ok, "b done"));

        var run = await CookbookExecutor.RunStepsAsync(cookbook, Context([]), CancellationToken.None);

        Assert.True(run.Ok);
        Assert.Equal("Bring up ok", run.Note);
        Assert.Null(run.FailedStep);
        Assert.Null(run.FailedStepTitle);
        Assert.Null(run.Failure);
    }

    [Fact]
    public async Task SingleStepSuccessKeepsTheStepNote() {
        var cookbook = new Plan("install-app", "Install app",
            new FixedStep("install-app", "Install app", CookbookStepStatus.Ok, "installed 3 split(s)"));

        var run = await CookbookExecutor.RunStepsAsync(cookbook, Context([]), CancellationToken.None);

        Assert.Equal("installed 3 split(s)", run.Note);
    }

    [Fact]
    public async Task SoftStepFailureDoesNotFailTheRun() {
        var cookbook = new Plan("bring-up", "Bring up",
            new SoftStep(new FixedStep("a", "A", CookbookStepStatus.Failed, "meh")),
            new FixedStep("b", "B", CookbookStepStatus.Ok, null));

        var run = await CookbookExecutor.RunStepsAsync(cookbook, Context([]), CancellationToken.None);

        Assert.True(run.Ok);
        Assert.Null(run.FailedStep);
        Assert.Equal(2, run.Steps.Count);
    }

    [Fact]
    public void FailureWithoutANoteNamesTheStep() {
        var run = new DeviceCookbookRun(false, "x", [], "s", null, "Step S");

        Assert.Equal("Step S failed", run.Failure);
    }

    [Fact]
    public void FailureWithoutAStepIsTheNoteAlone() {
        var run = new DeviceCookbookRun(false, "x", [], null, "unknown device");

        Assert.Equal("unknown device", run.Failure);
    }
}
