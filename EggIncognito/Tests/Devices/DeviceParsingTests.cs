using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class DeviceParsingTests {
    private const string Dumpsys = "    versionCode=111344 minSdk=24 targetSdk=36\n    versionName=1.35.7\n";

    private const string Plist = """
                                 <?xml version="1.0" encoding="UTF-8"?>
                                 <plist version="1.0">
                                 <array>
                                   <dict>
                                     <key>CFBundleIdentifier</key><string>com.WA5H2B7E4G.com.rileytestut.AltStore</string>
                                     <key>CFBundleShortVersionString</key><string>1.6</string>
                                   </dict>
                                   <dict>
                                     <key>CFBundleIdentifier</key><string>com.auxbrain.egginc</string>
                                     <key>CFBundleShortVersionString</key><string>1.35.8</string>
                                     <key>CFBundleVersion</key><string>1.35.8.0</string>
                                   </dict>
                                 </array>
                                 </plist>
                                 """;

    private const string ListCsv = "com.auxbrain.egginc, \"1.35.8\", \"Egg, Inc.\"\n";

    [Fact]
    public void AndroidVersion_ParsesNameAndCode() {
        (string? app, string? build) = DeviceParsing.AndroidVersion(Dumpsys);
        Assert.Equal("1.35.7", app);
        Assert.Equal("111344", build);
    }

    [Fact]
    public void AndroidVersion_NoMatch_ReturnsNulls() {
        (string? app, string? build) = DeviceParsing.AndroidVersion("garbage");
        Assert.Null(app);
        Assert.Null(build);
    }

    [Fact]
    public void IosVersion_Plist_FindsShortVersionAndBuild() {
        (string? app, string? build) = DeviceParsing.IosVersion(Plist, "com.auxbrain.egginc");
        Assert.Equal("1.35.8", app);
        Assert.Equal("1.35.8.0", build);
    }

    [Fact]
    public void IosVersion_Csv_HasNoBuild() {
        (string? app, string? build) = DeviceParsing.IosVersion(ListCsv, "com.auxbrain.egginc");
        Assert.Equal("1.35.8", app);
        Assert.Null(build);
    }

    [Fact]
    public void IosAppVersion_BundleNotInstalled_ReturnsNull() =>
        Assert.Null(DeviceParsing.IosAppVersion(Plist, "com.does.not.exist"));

    [Fact]
    public void IosAppVersion_Garbage_ReturnsNull() =>
        Assert.Null(DeviceParsing.IosAppVersion("not xml or csv", "com.auxbrain.egginc"));
}
