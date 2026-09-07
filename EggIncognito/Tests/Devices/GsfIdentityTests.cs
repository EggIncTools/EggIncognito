using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class GsfIdentityTests {
    [Fact]
    public void Parse_ReadsGservicesRow() {
        Assert.Equal("123456", GsfIdentity.Parse("Row: 0 value=123456\n"));
    }

    [Fact]
    public void Parse_FallsBackToCheckinPrefs() {
        const string xml = "<?xml version='1.0' encoding='utf-8' standalone='yes' ?>\n<map>\n"
                           + "    <long name=\"CheckinService_lastCheckinSuccessTime\" value=\"1788738671447\" />\n"
                           + "    <string name=\"android_id\">3991350912597918270</string>\n"
                           + "</map>\n";

        Assert.Equal("3991350912597918270", GsfIdentity.Parse(xml));
    }

    [Fact]
    public void Parse_IgnoresLongPrefsThatAlsoSayValue() {
        const string xml = "<map>\n    <long name=\"CheckinService_lastCheckinServerTime\" value=\"1788738671409\" />\n</map>\n";

        Assert.Null(GsfIdentity.Parse(xml));
    }

    [Fact]
    public void Parse_NullWhenNeitherSourceHasIt() {
        Assert.Null(GsfIdentity.Parse("cat: /data/data/com.google.android.gms/shared_prefs/Checkin.xml: No such file or directory\n"));
        Assert.Null(GsfIdentity.Parse(""));
    }
}
