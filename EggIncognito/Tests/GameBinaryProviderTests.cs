using EggIncognito.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public class GameBinaryProviderTests {
    private sealed class EmptyServices : IServiceProvider {
        public object? GetService(Type serviceType) => null;
    }

    private static GameBinaryProvider Provider(IReadOnlyDictionary<string, string?> settings) {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new GameBinaryProvider(new EmptyServices(), config, NullLogger<GameBinaryProvider>.Instance, TimeProvider.System);
    }

    [Fact]
    public async Task Extraction_NoDeviceNoStoreNoStash_FailsCleanly() {
        var p = Provider(new Dictionary<string, string?> {
            [DecompConfigKeys.SymbolizedIpaDir] =
                Path.Combine(Path.GetTempPath(), "egi-nonexistent-stash-" + Guid.NewGuid())
        });

        (bool ok, byte[]? bin, _, _, string? diag) = await p.GetExtractionBinaryAsync(CancellationToken.None);

        Assert.False(ok);
        Assert.Null(bin);
        Assert.NotNull(diag);
    }

    [Fact]
    public async Task Extraction_OverridePathWins_OverDeviceAndStash() {
        string path = Path.Combine(Path.GetTempPath(), "egi-override-" + Guid.NewGuid() + ".bin");
        byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8];
        await File.WriteAllBytesAsync(path, payload);
        try {
            var p = Provider(new Dictionary<string, string?> { [DecompConfigKeys.BinaryPath] = path });

            (bool ok, byte[]? bin, _, string version, _) = await p.GetExtractionBinaryAsync(CancellationToken.None);

            Assert.True(ok);
            Assert.Equal(payload, bin);
            Assert.Equal("override", version);
        } finally {
            File.Delete(path);
        }
    }
}
