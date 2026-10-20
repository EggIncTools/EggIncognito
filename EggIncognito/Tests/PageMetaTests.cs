using EggIncognito.Services;

namespace EggIncognito.Tests;

public class PageMetaTests {
    [Theory]
    [InlineData("/ios/1.37.0.1", "ei.proto - iOS 1.37.0.1")]
    [InlineData("/android/111358", "ei.proto - Android 111358")]
    [InlineData("/IOS/1.37.0.1", "ei.proto - iOS 1.37.0.1")]
    public void AVersionPathGetsItsOwnTitle(string path, string title) => Assert.Equal(title, PageMeta.For(path).Title);

    [Fact]
    public void AVersionDescriptionNamesThePlatformAndBuild() {
        string description = PageMeta.For("/ios/1.37.0.1").Description;
        Assert.Contains("iOS", description, StringComparison.Ordinal);
        Assert.Contains("1.37.0.1", description, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtoDataKeepsTheRegistryCard() => Assert.Equal("EggIncognito - Proto Registry", PageMeta.For("/protodata").Title);

    [Theory]
    [InlineData("/ios/<script>")]
    [InlineData("/ios/a b")]
    [InlineData("/i0s/1.37.0.1")]
    [InlineData("/ios/")]
    [InlineData("/ios/1.37.0.1/extra")]
    public void JunkFallsBackToTheDefaultInsteadOfReflecting(string path) => Assert.Equal(PageMeta.Default.Title, PageMeta.For(path).Title);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/nope")]
    public void UnknownPathsGetTheDefault(string? path) => Assert.Equal(PageMeta.Default.Title, PageMeta.For(path).Title);
}
