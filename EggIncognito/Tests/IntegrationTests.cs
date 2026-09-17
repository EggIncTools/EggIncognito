using Ei;
using Google.Protobuf;
using Microsoft.AspNetCore.Hosting;

namespace EggIncognito.Tests;

public sealed class EggIncApiFactory : EgiTestFactory {
    protected override void Configure(IWebHostBuilder builder) {
        builder.UseEnvironment("Testing");
        builder.UseSetting("RateLimiting:Enabled", "true");
        builder.UseSetting("EndpointsPath", TestPaths.FixturesDir());
    }
}

[Collection(EggIncApiCollection.Name)]
public class IntegrationTests(EggIncApiFactory factory) {
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Response_HasTextHtmlContentType() {
        var response = await _client.PostAsync("/ei/first_contact_secure", MakeFormContent(null));
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task FirstContactSecure_NoEid_ReturnsDefaultEndpoint() {
        var response = await _client.PostAsync("/ei/first_contact_secure", MakeFormContent(null));
        response.EnsureSuccessStatusCode();

        var result = await DecodeResponse<EggIncFirstContactResponse>(response);

        Assert.Equal("EI0000000000000001", result.EiUserId);
        Assert.Equal("MockPlayer", result.Backup.UserName);
        Assert.Equal(50ul, result.Backup.Game.EggsOfProphecy);
    }

    [Fact]
    public async Task FirstContactSecure_WithKnownEid_ReturnsEidEndpoint() {
        const string eid = "EI0000000000000002";
        var response = await _client.PostAsync("/ei/first_contact_secure", MakeFormContent(eid));
        response.EnsureSuccessStatusCode();

        var result = await DecodeResponse<EggIncFirstContactResponse>(response);

        Assert.Equal(eid, result.EiUserId);
        Assert.Equal("TestPlayer2", result.Backup.UserName);
        Assert.Equal(224ul, result.Backup.Game.EggsOfProphecy);
    }

    [Fact]
    public async Task FirstContactSecure_WithUnknownEid_FallsBackToDefaultEndpoint() {
        var response = await _client.PostAsync("/ei/first_contact_secure", MakeFormContent("EI9999999999999999"));
        response.EnsureSuccessStatusCode();

        var result = await DecodeResponse<EggIncFirstContactResponse>(response);

        Assert.Equal("EI0000000000000001", result.EiUserId);
    }

    [Fact]
    public async Task GetPeriodicals_DefaultEndpoint_ReturnsTwoEvents() {
        var response = await _client.PostAsync("/ei/get_periodicals", MakeFormContent(null));
        response.EnsureSuccessStatusCode();

        var result = await DecodeResponse<PeriodicalsResponse>(response);

        Assert.Equal(2, result.Events.Events.Count);
    }

    [Fact]
    public async Task GetPeriodicals_WithKnownEid_ReturnsThreeEvents() {
        var response = await _client.PostAsync("/ei/get_periodicals", MakeFormContent("EI0000000000000002"));
        response.EnsureSuccessStatusCode();

        var result = await DecodeResponse<PeriodicalsResponse>(response);

        Assert.Equal(3, result.Events.Events.Count);
    }

    [Fact]
    public async Task LaunchMission_ReturnsHenerpriseShip() {
        var response = await _client.PostAsync("/ei_afx/launch_mission", MakeFormContent(null));
        response.EnsureSuccessStatusCode();

        var result = await DecodeResponse<MissionResponse>(response);

        Assert.True(result.Success);
        Assert.Equal(MissionInfo.Types.Spaceship.Henerprise, result.Info.Ship);
        Assert.Equal(MissionInfo.Types.Status.Exploring, result.Info.Status);
    }

    [Fact]
    public async Task GetContracts_ReturnsActiveContracts() {
        var response = await _client.PostAsync("/ei/get_contracts", MakeFormContent(null));
        response.EnsureSuccessStatusCode();

        var result = await DecodeResponse<ContractsResponse>(response);

        Assert.True(result.Contracts.Count > 0);
    }

    [Fact]
    public async Task AllEndpoints_DoNotReturn500() {
        string[] paths = [
            "/ei/first_contact_secure",
            "/ei/get_periodicals",
            "/ei/get_contracts",
            "/ei/get_events",
            "/ei/daily_gift_info",
            "/ei_afx/launch_mission",
            "/ei_afx/sync_mission",
            "/ei/coop_status"
        ];

        foreach (string path in paths) {
            var response = await _client.PostAsync(path, MakeFormContent("EI0000000000000002"));
            Assert.True(
                (int)response.StatusCode < 500,
                $"POST {path} returned {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Sim_ServerError_Returns500() {
        var response = await _client.PostAsync("/ei/first_contact_secure?sim=server_error", MakeFormContent(null));
        Assert.Equal(500, (int)response.StatusCode);
    }

    [Fact]
    public async Task Sim_Empty_Returns200WithValidBase64Proto() {
        var response = await _client.PostAsync("/ei/first_contact_secure?sim=empty", MakeFormContent(null));
        Assert.Equal(200, (int)response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        byte[] bytes = Convert.FromBase64String(body);
        var msg = AuthenticatedMessage.Parser.ParseFrom(bytes);
        Assert.NotNull(msg);
    }

    [Fact]
    public async Task Sim_Corrupt_Returns200WithInvalidBase64() {
        var response = await _client.PostAsync("/ei/first_contact_secure?sim=corrupt", MakeFormContent(null));
        Assert.Equal(200, (int)response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Throws<FormatException>(() => Convert.FromBase64String(body));
    }

    [Fact]
    public async Task Sim_RateLimited_Returns429WithRetryAfterHeader() {
        var response = await _client.PostAsync("/ei/first_contact_secure?sim=rate_limited", MakeFormContent(null));
        Assert.Equal(429, (int)response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"));
        Assert.Equal("60", response.Headers.GetValues("Retry-After").First());
    }

    [Fact]
    public async Task Sim_UnknownName_Returns400WithErrorJson() {
        var response =
            await _client.PostAsync("/ei/first_contact_secure?sim=not_a_real_behavior", MakeFormContent(null));
        Assert.Equal(400, (int)response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("unknown sim", body);
        Assert.Contains("server_error", body);
    }

    [Fact]
    public async Task Options_Root_Returns200WithAllBehaviors() {
        var request = new HttpRequestMessage(HttpMethod.Options, "/");
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("server_error", body);
        Assert.Contains("empty", body);
        Assert.Contains("corrupt", body);
        Assert.Contains("httpStatus", body);
    }

    [Fact]
    public async Task Options_Slug_Returns200WithApplicableBehaviors() {
        var request = new HttpRequestMessage(HttpMethod.Options, "/ei/first_contact_secure");
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("server_error", body);
    }

    private static FormUrlEncodedContent MakeFormContent(string? eid) {
        var msg = new AuthenticatedMessage();
        if (!string.IsNullOrEmpty(eid))
            msg.UserId = eid;
        string encoded = Convert.ToBase64String(msg.ToByteArray());
        return new FormUrlEncodedContent([new KeyValuePair<string, string>("data", encoded)]);
    }

    private static async Task<T> DecodeResponse<T>(HttpResponseMessage response)
        where T : IMessage<T>, new() {
        string body = await response.Content.ReadAsStringAsync();
        byte[] bytes = Convert.FromBase64String(body);
        return new MessageParser<T>(() => new T()).ParseFrom(bytes);
    }
}
