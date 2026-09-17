using System.Net;

namespace EggIncognito.Tests;

[Collection(HostedAppCollection.Name)]
public class StoredEndpointGateTests(HostedAppFactory f) {
    [Fact]
    public async Task Hosted_UpsertEndpoint_Is403() {
        var c = f.CreateClient();
        var r = await c.PostAsJsonAsync("/api/db/endpoint",
            new {
                path = "ei/get_periodicals",
                eid = (string?)null,
                responseJson = "{}",
                responseType = "PeriodicalsResponse"
            });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Hosted_AddRoute_Is403() {
        var c = f.CreateClient();
        var r = await c.PostAsJsonAsync("/api/db/route",
            new {
                path = "ei/new",
                requestType = (string?)null,
                responseType = "PeriodicalsResponse",
                requestWrapped = false,
                responseWrapped = false,
                rawResponse = (string?)null,
                pathParam = false,
                pathParamOnly = false
            });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Reads_AreReachable_EmptyWhenNoDb() {
        var c = f.CreateClient();
        var r = await c.GetAsync("/api/db/endpoints");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }
}
