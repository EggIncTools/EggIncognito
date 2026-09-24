using System.Net;

namespace EggIncognito.Services;

public interface ISealedProxy {
    bool IsConfigured { get; }

    Task<bool> CanUseAsync(ICurrentUser user, CancellationToken ct = default);

    HttpClient CreateEgressClient();
}

public sealed record SealedProxyOptions {
    public string UpstreamUrl { get; init; } = "";
    public string? Username { get; init; }
    public string? Password { get; init; }

    public static SealedProxyOptions FromConfig(IConfiguration config) => new() {
        UpstreamUrl = config["SealedProxy:UpstreamUrl"] ?? "",
        Username = config["SealedProxy:Username"],
        Password = config["SealedProxy:Password"]
    };
}

public sealed class SealedProxy(
    SealedProxyOptions options,
    IHttpClientFactory httpFactory) : ISealedProxy {
    public const string EgressClientName = "sealed-egress";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.UpstreamUrl);

    public Task<bool> CanUseAsync(ICurrentUser user, CancellationToken ct = default) =>
        Task.FromResult(IsConfigured && user.IsAuthenticated &&
                        !string.IsNullOrEmpty(user.DiscordId) && user.IsSupporter);

    public HttpClient CreateEgressClient() => httpFactory.CreateClient(EgressClientName);

    public static IWebProxy? BuildProxy(SealedProxyOptions options) {
        if (string.IsNullOrWhiteSpace(options.UpstreamUrl)) return null;
        if (!Uri.TryCreate(options.UpstreamUrl, UriKind.Absolute, out var uri)) return null;
        var proxy = new WebProxy(uri);
        if (!string.IsNullOrEmpty(options.Username))
            proxy.Credentials = new NetworkCredential(options.Username, options.Password ?? "");
        return proxy;
    }
}
