using EggIncognito.Bot;
using EggIncognito.Core;

namespace EggIncognito.Tests;

public class ProtoTruncationTests {
    private static string Truncate(string text, int max = ProtoQuery.MaxDescription) =>
        Strings.Truncate(text, max, ProtoQuery.TruncatedMarker);

    [Fact]
    public void Truncate_ShortText_Unchanged() => Assert.Equal("hello", Truncate("hello"));

    [Fact]
    public void Truncate_ExactlyMax_Unchanged() {
        string text = new('x', ProtoQuery.MaxDescription);
        Assert.Same(text, Truncate(text));
    }

    [Fact]
    public void Truncate_LongText_ClampedWithMarker() {
        string text = new('x', ProtoQuery.MaxDescription + 5000);
        string result = Truncate(text);
        Assert.Equal(ProtoQuery.MaxDescription, result.Length);
        Assert.EndsWith("(truncated)", result);
    }

    [Fact]
    public void Truncate_CustomBudget_Respected() {
        string result = Truncate(new string('x', 100), 50);
        Assert.Equal(50, result.Length);
        Assert.EndsWith("(truncated)", result);
    }

    [Fact]
    public void Truncate_DefaultBudget_FitsEmbedLimitWithCodeFence() {
        string fenced = "```\n" + Truncate(new string('x', 50_000)) + "\n```";
        Assert.True(fenced.Length <= 4096);
    }

    [Fact]
    public void Truncate_Null_ReturnsEmpty() => Assert.Equal("", Strings.Truncate(null, 10));
}
