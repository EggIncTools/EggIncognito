using System.Net;
using EggIncognito.Components.Shared;

namespace EggIncognito.Tests;

public class SelfCallJsonTests {
    private const string Url = "http://localhost/api/things";

    [Fact]
    public async Task ListAsync_ReturnsTheList() {
        var client = ClientFor(_ => Json(HttpStatusCode.OK, """[{"name":"a"},{"name":"b"}]"""));

        var rows = await client.ListAsync<Thing>(Url);

        Assert.Equal(2, rows.Count);
        Assert.Equal("a", rows[0].Name);
    }

    [Fact]
    public async Task TryListAsync_ReturnsOkAndTheList() {
        var client = ClientFor(_ => Json(HttpStatusCode.OK, """[{"name":"a"},{"name":"b"}]"""));

        (bool ok, var rows) = await client.TryListAsync<Thing>(Url);

        Assert.True(ok);
        Assert.Equal(2, rows.Count);
        Assert.Equal("a", rows[0].Name);
    }

    [Fact]
    public async Task TryListAsync_ReportsFailureOnNotFound() {
        var client = ClientFor(_ => Json(HttpStatusCode.NotFound, """{"error":"nope"}"""));

        (bool ok, var rows) = await client.TryListAsync<Thing>(Url);

        Assert.False(ok);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task TryListAsync_ReportsFailureOnMalformedJson() {
        var client = ClientFor(_ => Json(HttpStatusCode.OK, "{not json"));

        (bool ok, var rows) = await client.TryListAsync<Thing>(Url);

        Assert.False(ok);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task TryListAsync_ReportsFailureWhenTheTransportThrows() {
        var client = ClientFor(_ => throw new HttpRequestException("boom"));

        (bool ok, var rows) = await client.TryListAsync<Thing>(Url);

        Assert.False(ok);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task TryListAsync_JsonNullIsOkAndEmpty() {
        var client = ClientFor(_ => Json(HttpStatusCode.OK, "null"));

        (bool ok, var rows) = await client.TryListAsync<Thing>(Url);

        Assert.True(ok);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task OneAsync_ReturnsTheObject() {
        var client = ClientFor(_ => Json(HttpStatusCode.OK, """{"name":"solo"}"""));

        var thing = await client.OneAsync<Thing>(Url);

        Assert.NotNull(thing);
        Assert.Equal("solo", thing.Name);
    }

    [Fact]
    public async Task OneAsync_ReturnsNullOnNotFound() {
        var client = ClientFor(_ => Json(HttpStatusCode.NotFound, ""));

        Assert.Null(await client.OneAsync<Thing>(Url));
    }

    [Fact]
    public async Task OneAsync_ReturnsNullOnMalformedJson() {
        var client = ClientFor(_ => Json(HttpStatusCode.OK, "{not json"));

        Assert.Null(await client.OneAsync<Thing>(Url));
    }

    [Fact]
    public async Task OneAsync_ReturnsNullWhenTheTransportThrows() {
        var client = ClientFor(_ => throw new HttpRequestException("boom"));

        Assert.Null(await client.OneAsync<Thing>(Url));
    }

    [Fact]
    public void Web_IsCamelCase() {
        Assert.NotNull(SelfCallJson.Web.PropertyNamingPolicy);
        Assert.True(SelfCallJson.Web.PropertyNameCaseInsensitive);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => StubResponses.Json(status, body);

    private static HttpClient ClientFor(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new StubHttpFactory(new StubHttpMessageHandler(respond)).CreateClient("self");

    private sealed record Thing(string Name);
}
