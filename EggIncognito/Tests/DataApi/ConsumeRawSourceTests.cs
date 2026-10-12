using EggIncognito.Services.DataApi;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace EggIncognito.Tests.DataApi;

public sealed class ConsumeRawSourceTests {
    private static QueryCollection Q(params (string K, string V)[] kv) =>
        new(kv.ToDictionary(p => p.K, p => new StringValues(p.V)));

    [Fact]
    public void Defaults() => Assert.Equal((0L, 1000), ConsumeRawSource.Paging(Q()));

    [Theory]
    [InlineData("0", 1)]
    [InlineData("99999", 5000)]
    [InlineData("abc", 1000)]
    public void LimitClamps(string limit, int expected) =>
        Assert.Equal(expected, ConsumeRawSource.Paging(Q(("limit", limit))).Limit);

    [Theory]
    [InlineData("-5", 0)]
    [InlineData("x", 0)]
    [InlineData("42", 42)]
    public void AfterParses(string after, long expected) =>
        Assert.Equal(expected, ConsumeRawSource.Paging(Q(("after", after))).After);

    [Fact]
    public void MalformedJsonBecomesEmptyArray() => Assert.Empty(ConsumeRawSource.ArrayOf("{nope"));
}
