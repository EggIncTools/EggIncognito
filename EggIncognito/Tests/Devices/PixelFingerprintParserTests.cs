using EggIncognito.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class PixelFingerprintParserTests {
    private const string VersionsHtml = """
        <a href="https://developer.android.com/about/versions/15">Android 15</a>
        <a href="https://developer.android.com/about/versions/16">Android 16</a>
        <a href="https://developer.android.com/about/versions/16">Android 16 again</a>
        <a href="https://developer.android.com/about/versions/16/get">Get</a>
        """;

    private const string LatestHtml = """
        <a href="/about/versions/16/release-notes">notes</a>
        <a href="/about/versions/16/download-qpr">QPR beta</a>
        <a href="/about/versions/16/download">factory</a>
        """;

    private const string FiHtml = """
        <table>
        <tr id="oriole">
        <td>Pixel 6</td><td>oriole-bp31.250610.004</td>
        </tr>
        <tr id="husky">
        <td>Pixel 8 Pro</td>
        </tr>
        </table>
        """;

    private const string FlashHtml = """
        <html><body data-client-config="a;KEYVALUE&amp;b">x</body></html>
        """;

    private const string BuildsJson = """
        {
          "builds": [
            {
              "releaseCandidateName": "AP31.250610.004",
              "buildId": "13000001",
              "releaseTrackVersionName": "Android 16 QPR1",
              "factoryImageDownloadUrl": "https://dl.google.com/dl/android/aosp/old.zip",
              "canary": false
            },
            {
              "releaseCandidateName": "CP11.250801.007",
              "buildId": "13500002",
              "releaseTrackVersionName": "Android 17 Canary",
              "factoryImageDownloadUrl": "https://dl.google.com/dl/android/aosp/canary.zip",
              "canary": true
            }
          ]
        }
        """;

    [Fact]
    public void LatestVersionUrl_PicksHighestDistinctLink() =>
        Assert.Equal("https://developer.android.com/about/versions/16", PixelFingerprintParser.LatestVersionUrl(VersionsHtml));

    [Fact]
    public void LatestVersionUrl_ReturnsNullWithoutLinks() =>
        Assert.Null(PixelFingerprintParser.LatestVersionUrl("<html></html>"));

    [Fact]
    public void QprDownloadPath_PicksTheQprDownloadHref() =>
        Assert.Equal("/about/versions/16/download-qpr", PixelFingerprintParser.QprDownloadPath(LatestHtml));

    [Fact]
    public void QprDownloadPath_ReturnsNullWhenAbsent() =>
        Assert.Null(PixelFingerprintParser.QprDownloadPath("<a href=\"/about/versions/16/download\">x</a>"));

    [Fact]
    public void Devices_PairsProductWithFirstCellInDocumentOrder() {
        var devices = PixelFingerprintParser.Devices(FiHtml);
        var expected = new[] { ("oriole_beta", "Pixel 6"), ("husky_beta", "Pixel 8 Pro") };
        Assert.Equal(expected, devices);
    }

    [Fact]
    public void FlashKey_TakesTextBetweenSemicolonAndAmpersand() =>
        Assert.Equal("KEYVALUE", PixelFingerprintParser.FlashKey(FlashHtml));

    [Fact]
    public void FlashKey_ReturnsNullWithoutBodyConfig() =>
        Assert.Null(PixelFingerprintParser.FlashKey("<body>plain</body>"));

    [Fact]
    public void Canary_ReturnsLastCanaryBuild() {
        var canary = PixelFingerprintParser.Canary(BuildsJson);
        Assert.NotNull(canary);
        Assert.Equal("CP11.250801.007", canary.ReleaseCandidateName);
        Assert.Equal("13500002", canary.BuildId);
        Assert.Equal("Android 17 Canary", canary.ReleaseTrackVersionName);
        Assert.Equal("https://dl.google.com/dl/android/aosp/canary.zip", canary.FactoryImageDownloadUrl);
    }

    [Fact]
    public void Canary_ReadsFlashStationShape_WithCanaryUnderPreviewMetadata() {
        const string json = """
            {"flashstationBuild":[
              {"product":"oriole_beta","buildId":"15760424","releaseCandidateName":"ZP11.260618.005",
               "factoryImageDownloadUrl":"https://dl.google.com/developers/android/CANARY/images/factory/a.zip",
               "target":"oriole_beta-user",
               "previewMetadata":{"id":"canary-202607","releaseTrackName":"Android Canary","releaseTrackVersionName":"Canary 202607","active":false,"canary":true}},
              {"product":"oriole_beta","buildId":"16064790","releaseCandidateName":"CP31.260623.012",
               "previewMetadata":{"id":"beta","releaseTrackName":"Android Beta","releaseTrackVersionName":"Beta 3","active":true}},
              {"product":"oriole_beta","buildId":"16004061","releaseCandidateName":"ZP11.260717.006",
               "factoryImageDownloadUrl":"https://dl.google.com/developers/android/CANARY/images/factory/b.zip",
               "previewMetadata":{"id":"canary-202608","releaseTrackName":"Android Canary","releaseTrackVersionName":"Canary 202608","active":true,"canary":true}}
            ]}
            """;

        var canary = PixelFingerprintParser.Canary(json);

        Assert.NotNull(canary);
        Assert.Equal("ZP11.260717.006", canary.ReleaseCandidateName);
        Assert.Equal("16004061", canary.BuildId);
        Assert.Equal("Canary 202608", canary.ReleaseTrackVersionName);
        Assert.EndsWith("/b.zip", canary.FactoryImageDownloadUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void Canary_HandlesBareArrayRoot() {
        const string json = """
            [{"releaseCandidateName":"X","buildId":"1","canary":true},{"releaseCandidateName":"Y","buildId":"2","canary":true}]
            """;
        Assert.Equal("Y", PixelFingerprintParser.Canary(json)?.ReleaseCandidateName);
    }

    [Fact]
    public void Canary_ReturnsNullWithoutCanary() =>
        Assert.Null(PixelFingerprintParser.Canary("""{"builds":[{"releaseCandidateName":"X","buildId":"1","canary":false}]}"""));

    [Fact]
    public void Expiry_IsSixWeeksAfterRelease() =>
        Assert.Equal(new DateOnly(2026, 9, 16), PixelFingerprintParser.Expiry(new DateOnly(2026, 8, 5)));
}
