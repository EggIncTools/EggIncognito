using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class PifPropTests {
    private const string Legacy =
        "# Build Fields\n"
        + "MANUFACTURER=Google\n"
        + "MODEL=Pixel 6\n"
        + "FINGERPRINT=google/oriole_beta/oriole:CANARY/ZP11.260717.006/16004061:user/release-keys\n"
        + "BRAND=google\n"
        + "PRODUCT=oriole_beta\n"
        + "DEVICE=oriole\n"
        + "RELEASE=CANARY\n"
        + "ID=ZP11.260717.006\n"
        + "INCREMENTAL=16004061\n"
        + "TYPE=user\n"
        + "TAGS=release-keys\n"
        + "SECURITY_PATCH=2026-08-05\n"
        + "DEVICE_INITIAL_SDK_INT=32\n"
        + "\n"
        + "# System Properties\n"
        + "*.build.id=ZP11.260717.006\n"
        + "*.security_patch=2026-08-05\n"
        + "*api_level=32\n"
        + "\n"
        + "# Advanced Settings\n"
        + "verboseLogs=0\n"
        + "spoofApps=0\n"
        + "spoofBuild=1\n"
        + "spoofProps=1\n"
        + "spoofProvider=0\n"
        + "spoofSignature=0\n"
        + "spoofVendingFinger=0\n"
        + "spoofVendingSdk=0\n"
        + "spoofPixel=0\n"
        + " \n"
        + "# Released On: 2026-08-06\n"
        + "# Estimated Expiry: 2026-09-17\n";

    [Fact]
    public void Parse_LegacyProp_ReadsEveryField() {
        var profile = PifProp.Parse(Legacy);

        Assert.NotNull(profile);
        Assert.Equal("Google", profile.Manufacturer);
        Assert.Equal("Pixel 6", profile.Model);
        Assert.Equal("google", profile.Brand);
        Assert.Equal("oriole_beta", profile.Product);
        Assert.Equal("oriole", profile.Device);
        Assert.Equal("CANARY", profile.Release);
        Assert.Equal("ZP11.260717.006", profile.Id);
        Assert.Equal("16004061", profile.Incremental);
        Assert.Equal("2026-08-05", profile.SecurityPatch);
        Assert.Equal(32, profile.DeviceInitialSdkInt);
        Assert.Equal(new DateOnly(2026, 8, 6), profile.ReleasedOn);
        Assert.Equal(new DateOnly(2026, 9, 17), profile.Expiry);
        Assert.Equal("google/oriole_beta/oriole:CANARY/ZP11.260717.006/16004061:user/release-keys", profile.Fingerprint);
    }

    [Fact]
    public void Render_RoundTripsLegacyProp() {
        var profile = PifProp.Parse(Legacy);
        Assert.NotNull(profile);

        string rendered = PifProp.Render(profile);

        Assert.Equal(Legacy.Replace("\n \n", "\n\n", StringComparison.Ordinal), rendered);
        Assert.Equal(profile, PifProp.Parse(rendered));
    }

    [Fact]
    public void Render_UnknownDates_WriteUnknown() {
        var profile = PifProp.Parse(Legacy)! with { ReleasedOn = null, Expiry = null };

        string rendered = PifProp.Render(profile);

        Assert.EndsWith("# Released On: Unknown\n# Estimated Expiry: Unknown\n", rendered, StringComparison.Ordinal);
        Assert.Null(PifProp.Parse(rendered)!.Expiry);
    }

    [Fact]
    public void Parse_MissingModel_ReturnsNull() {
        string text = Legacy.Replace("MODEL=Pixel 6\n", "", StringComparison.Ordinal);

        Assert.Null(PifProp.Parse(text));
    }

    [Fact]
    public void Expired_ComparesAgainstExpiry() {
        var profile = PifProp.Parse(Legacy);
        Assert.NotNull(profile);

        Assert.False(profile.Expired(new DateOnly(2026, 9, 17)));
        Assert.True(profile.Expired(new DateOnly(2026, 9, 18)));
        Assert.False((profile with { Expiry = null }).Expired(new DateOnly(2030, 1, 1)));
    }
}
