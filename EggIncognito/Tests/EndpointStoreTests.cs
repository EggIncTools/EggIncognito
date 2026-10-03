using EggIncognito.Core.Services;
using Ei;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public sealed class EndpointStoreTests : IDisposable {
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void DoesNotThrowWhenEndpointsDirMissing() {
        var store = new EndpointStore(
            new FileEndpointSource(_tmp.File("does_not_exist")),
            null,
            NullLogger<EndpointStore>.Instance);
        var result = store.Fetch<AuthenticatedMessage>("ei/any");
        Assert.NotNull(result);
    }
}
