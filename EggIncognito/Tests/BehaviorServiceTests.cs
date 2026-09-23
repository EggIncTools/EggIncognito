using System.Text;
using EggIncognito.Core.Services;
using Ei;

namespace EggIncognito.Tests;

public class BehaviorServiceTests {
    private static IBehaviorService Build() => new BehaviorService();

    [Fact]
    public void Get_KnownName_ReturnsBehavior() {
        var svc = Build();
        var b = svc.Find("server_error");
        Assert.NotNull(b);
        Assert.Equal("server_error", b.Name);
        Assert.Equal(500, b.HttpStatus);
    }

    [Fact]
    public void Get_CaseInsensitive_ReturnsBehavior() {
        var svc = Build();
        Assert.NotNull(svc.Find("SERVER_ERROR"));
        Assert.NotNull(svc.Find("Server_Error"));
    }

    [Fact]
    public void Get_UnknownName_ReturnsNull() {
        var svc = Build();
        Assert.Null(svc.Find("does_not_exist"));
    }

    [Fact]
    public void All_ContainsExpectedNames() {
        var svc = Build();
        var names = svc.All().Select(b => b.Name).ToHashSet();
        foreach (string expected in new[]
                     { "server_error", "maintenance", "not_found", "unauthorized", "rate_limited", "empty", "corrupt" }) {
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public void ForEndpoint_UniversalBehaviors_ReturnedForAnySlug() {
        var svc = Build();
        var results = svc.ForEndpoint("ei/first_contact_secure");
        Assert.Contains(results, b => b.Name == "server_error");
        Assert.Contains(results, b => b.Name == "empty");
    }

    [Fact]
    public void ForEndpoint_EndpointRestricted_ReturnedOnlyForMatchingSlug() {
        var restricted = new SimulationBehavior("test_only", "Test", 200, Endpoints: ["ei/test"]);
        var testSvc = new BehaviorService(new[] { restricted });
        Assert.Single(testSvc.ForEndpoint("ei/test"));
        Assert.Empty(testSvc.ForEndpoint("ei/other"));
    }

    [Fact]
    public void RateLimited_HasRetryAfterHeader() {
        var svc = Build();
        var b = svc.Find("rate_limited");
        Assert.NotNull(b?.ExtraHeaders);
        Assert.Equal("60", b.ExtraHeaders!["Retry-After"]);
    }

    [Fact]
    public void Empty_BodyDecodesToValidProto() {
        var svc = Build();
        var b = svc.Find("empty");
        Assert.NotNull(b?.Body);
        byte[] bytes = Convert.FromBase64String(Encoding.UTF8.GetString(b.Body!()));
        var msg = AuthenticatedMessage.Parser.ParseFrom(bytes);
        Assert.NotNull(msg);
    }

    [Fact]
    public void Corrupt_BodyIsInvalidBase64() {
        var svc = Build();
        var b = svc.Find("corrupt");
        Assert.NotNull(b?.Body);
        string bodyStr = Encoding.UTF8.GetString(b.Body!());
        Assert.Throws<FormatException>(() => Convert.FromBase64String(bodyStr));
    }
}
