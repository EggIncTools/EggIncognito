using EggIncognito.Controllers;
using EggIncognito.Data.Services;
using EggIncognito.Models.Protos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Tests;

public class ProtoBatchApiTests {
    private static readonly ProtoRegistryController Controller = new(new FakeUser(), TimeProvider.System);

    [Fact]
    public async Task OfferBatch_RejectsEmptyAndOversizedItemLists() {
        (var staged, var files) = Stores();

        Assert.Equal(400, Assert.IsType<ObjectResult>(
            await Controller.StagedOfferBatch(new OfferBatchRequest([]), staged, files, CancellationToken.None)).StatusCode);
        Assert.Equal(400, Assert.IsType<ObjectResult>(
            await Controller.StagedOfferBatch(new OfferBatchRequest([.. Enumerable.Repeat(Item(), 501)]), staged, files,
                CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task CheckBatch_WithoutStore_ReturnsOneOutcomePerItem() {
        var result = await Controller.StagedCheckBatch(
            new CheckBatchRequest([new CheckItem("a", "ios", null, null, null), new CheckItem("b", null, null, null, null)]),
            null, CancellationToken.None);

        var rows = Assert.IsType<List<StagedProtoStore.CheckOutcome>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task MergeBatch_RejectsEmptyGroupList() {
        Assert.Equal(400, Assert.IsType<ObjectResult>(
            await Controller.MergeBatch(new MergeBatchRequest([]), Registry(), CancellationToken.None)).StatusCode);
    }

    private static OfferItem Item() => new("ios", "1.0", "1", null, null, null, "message A {}");

    private static EggIncognitoDbContext Db() =>
        new(new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none").Options);

    private static ProtoRegistryStore Registry() => new(Db(), TimeProvider.System, []);

    private static (StagedProtoStore Staged, AnalyzedFileStore Files) Stores() {
        var registry = Registry();
        return (new StagedProtoStore(Db(), TimeProvider.System, registry),
            new AnalyzedFileStore(Db(), TimeProvider.System, registry));
    }
}
