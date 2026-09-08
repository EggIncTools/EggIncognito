using System.Text;
using EggIncognito.Capture;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class DeviceResponseOverrideStoreTests {
    private const string Route = "ei/first_contact_secure";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("<html>hello</html>");

    [Fact]
    public async Task For_NothingSet_AnswersNothing() {
        var store = new DeviceResponseOverrideStore();

        var answer = await Ask(store, "d1", Post("/ei/first_contact_secure"));

        Assert.Null(answer);
        Assert.False(store.Enabled("d1"));
        Assert.Empty(store.Paths("d1"));
    }

    [Fact]
    public async Task For_SetPath_AnswersWithDecodedBodyAndContentType() {
        var store = await Installed("d1", Route, "text/plain");

        var answer = await Ask(store, "d1", Post("/ei/first_contact_secure"));

        Assert.NotNull(answer);
        Assert.Equal(Body, answer.Body);
        Assert.Equal(200, answer.StatusCode);
        Assert.Equal("text/plain", answer.ContentType);
        Assert.True(store.Enabled("d1"));
        Assert.Contains(Route, store.Paths("d1"));
    }

    [Fact]
    public async Task For_EidSuffixInRequestPath_StillAnswers() {
        var store = await Installed("d1", Route);

        var answer = await Ask(store, "d1", Post("/ei/first_contact_secure/EI1234567890123456"));

        Assert.NotNull(answer);
        Assert.Equal(Body, answer.Body);
    }

    [Fact]
    public async Task For_LeadingSlashAndCaseInSetPath_Normalized() {
        var store = await Installed("d1", "/EI/First_Contact_Secure");

        var answer = await Ask(store, "d1", Post("/ei/first_contact_secure"));

        Assert.NotNull(answer);
    }

    [Fact]
    public async Task For_OtherDevice_AnswersNothing() {
        var store = await Installed("d1", Route);

        Assert.Null(await Ask(store, "d2", Post("/ei/first_contact_secure")));
    }

    [Fact]
    public void For_EmptyDeviceId_Null() {
        var store = new DeviceResponseOverrideStore();

        Assert.Null(store.For(""));
    }

    [Fact]
    public async Task For_Get_Ignored() {
        var store = await Installed("d1", Route);

        var answer = await Ask(store, "d1",
            new CaptureOverrideRequest("www.auxbrain.com", "GET", "/ei/first_contact_secure", null, null));

        Assert.Null(answer);
    }

    [Fact]
    public async Task For_NonAuxbrainHost_Ignored() {
        var store = await Installed("d1", Route);

        var answer = await Ask(store, "d1",
            new CaptureOverrideRequest("example.com", "POST", "/ei/first_contact_secure", null, null));

        Assert.Null(answer);
    }

    [Fact]
    public async Task For_UnrelatedPath_Ignored() {
        var store = await Installed("d1", Route);

        Assert.Null(await Ask(store, "d1", Post("/ei/save_backup")));
    }

    [Fact]
    public async Task Set_ReplacesPreviousSet() {
        var store = await Installed("d1", Route);

        var res = await store.SetAsync("d1", [Entry("ei/save_backup")], CancellationToken.None);

        Assert.True(res.Ok);
        Assert.Null(await Ask(store, "d1", Post("/ei/first_contact_secure")));
        Assert.NotNull(await Ask(store, "d1", Post("/ei/save_backup")));
        Assert.Single(store.Paths("d1"), "ei/save_backup");
    }

    [Fact]
    public async Task Clear_StopsAnswering() {
        var store = await Installed("d1", Route);

        var res = await store.ClearAsync("d1", CancellationToken.None);

        Assert.True(res.Ok);
        Assert.False(store.Enabled("d1"));
        Assert.Null(await Ask(store, "d1", Post("/ei/first_contact_secure")));
    }

    [Fact]
    public async Task Set_BadBase64_Error() {
        var store = new DeviceResponseOverrideStore();

        var res = await store.SetAsync("d1",
            [new DeviceResponseOverride(Route, "not base64!!")], CancellationToken.None);

        Assert.Equal(DeviceOutcome.Error, res.Outcome);
        Assert.False(store.Enabled("d1"));
    }

    [Fact]
    public async Task Set_EmptyPath_Error() {
        var store = new DeviceResponseOverrideStore();

        var res = await store.SetAsync("d1",
            [new DeviceResponseOverride("", Convert.ToBase64String(Body))], CancellationToken.None);

        Assert.Equal(DeviceOutcome.Error, res.Outcome);
    }

    [Fact]
    public void Composite_NoSources_Null() {
        var composite = new CompositeDeviceResponseSources([]);

        Assert.Null(composite.For("d1"));
    }

    [Fact]
    public async Task Composite_SingleSource_ReturnsItDirectly() {
        var store = await Installed("d1", Route);
        var composite = new CompositeDeviceResponseSources([store]);

        var source = composite.For("d1");

        Assert.NotNull(source);
        Assert.NotNull(await source.TryAnswerAsync(Post("/ei/first_contact_secure"), CancellationToken.None));
    }

    [Fact]
    public async Task Composite_ManySources_FirstNonNullWins() {
        var silent = new StubSources(new StubSource(null));
        var first = new StubSources(new StubSource(new CaptureOverrideResponse([1], 200, "a/first")));
        var second = new StubSources(new StubSource(new CaptureOverrideResponse([2], 200, "a/second")));
        var composite = new CompositeDeviceResponseSources([silent, new StubSources(null), first, second]);

        var source = composite.For("d1");
        Assert.NotNull(source);
        var answer = await source.TryAnswerAsync(Post("/ei/first_contact_secure"), CancellationToken.None);

        Assert.NotNull(answer);
        Assert.Equal("a/first", answer.ContentType);
        Assert.Equal(1, silent.Inner!.Calls);
        Assert.Equal(1, first.Inner!.Calls);
        Assert.Equal(0, second.Inner!.Calls);
    }

    private static async Task<DeviceResponseOverrideStore> Installed(
        string deviceId, string path, string contentType = "text/html") {
        var store = new DeviceResponseOverrideStore();
        var res = await store.SetAsync(deviceId, [Entry(path, contentType)], CancellationToken.None);
        Assert.True(res.Ok, res.Note);
        return store;
    }

    private static DeviceResponseOverride Entry(string path, string contentType = "text/html") =>
        new(path, Convert.ToBase64String(Body), contentType);

    private static CaptureOverrideRequest Post(string path) =>
        new("www.auxbrain.com", "POST", path, null, null);

    private static ValueTask<CaptureOverrideResponse?> Ask(
        DeviceResponseOverrideStore store, string deviceId, CaptureOverrideRequest request) {
        var source = store.For(deviceId);
        Assert.NotNull(source);
        return source.TryAnswerAsync(request, CancellationToken.None);
    }

    private sealed class StubSources(StubSource? inner) : IDeviceResponseSources {
        public StubSource? Inner => inner;
        public ICaptureResponseSource? For(string deviceId) => inner;
    }

    private sealed class StubSource(CaptureOverrideResponse? answer) : ICaptureResponseSource {
        public int Calls { get; private set; }

        public ValueTask<CaptureOverrideResponse?> TryAnswerAsync(CaptureOverrideRequest request, CancellationToken ct) {
            Calls++;
            return ValueTask.FromResult(answer);
        }
    }
}
