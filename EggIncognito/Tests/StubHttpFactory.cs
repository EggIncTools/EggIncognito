namespace EggIncognito.Tests;

public sealed class StubHttpFactory(HttpMessageHandler? handler = null, Uri? baseAddress = null) : IHttpClientFactory {
    public string? LastName { get; private set; }

    public HttpClient CreateClient(string name) {
        LastName = name;
        var client = handler is null ? new HttpClient() : new HttpClient(handler, false);
        if (baseAddress is not null)
            client.BaseAddress = baseAddress;
        return client;
    }
}
