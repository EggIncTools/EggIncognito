namespace EggIncognito.Artifacts.Tests;

public class ArtifactEffectsTests {
    [Theory]
    [InlineData(3, 2.0, "100%")]
    [InlineData(3, 1.1, "10%")]
    [InlineData(3, 0.95, "-5%")]
    [InlineData(3, 8.0, "8×")]
    [InlineData(3, 0.0, "")]
    [InlineData(12, 0.05, "5%")]
    [InlineData(20, 12.0, "12")]
    public void FormatValue_MatchesGameDimensionsFormatValue(int dimension, double magnitude, string expected) =>
        Assert.Equal(expected, ArtifactEffects.FormatValue(dimension, magnitude));

    [Theory]
    [InlineData(3, 2.0, "+")]
    [InlineData(3, 0.95, "")]
    [InlineData(3, 8.0, "")]
    [InlineData(12, 0.05, "+")]
    [InlineData(20, 12.0, "+")]
    [InlineData(29, 0.25, "+")]
    public void ModifyPrefix_MatchesGameDimensionsValueModifyPrefix(int dimension, double magnitude, string expected) =>
        Assert.Equal(expected, ArtifactEffects.ModifyPrefix(dimension, magnitude));

    [Fact]
    public void Plain_StripsEscapeCodes() => Assert.Equal("+100% away earnings",
        ArtifactEffects.Plain("\u001bg+100%\u001bw away earnings"));

    [Fact]
    public void Render_SubstitutesEveryPlaceholder() {
        string rendered = ArtifactEffects.Render("{prefix}{value} {label} {currency} {comma} {int100}", 3, 1.5,
            d => d == 3 ? "away earnings" : null, "gems");
        Assert.Equal("+50% away earnings gems 1 150", rendered);
    }
}
