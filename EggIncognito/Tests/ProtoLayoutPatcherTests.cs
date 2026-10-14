using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class ProtoLayoutPatcherTests {
    private const string Main = """
        syntax = "proto2";

        package ei;

        enum Platform {
            UNKNOWN_PLATFORM = 0;
            IOS = 1;
        }

        message MissionInfo {
            optional string id = 1;

            message Spaceship {
                optional uint32 level = 1;
            }
        }

        message Backup {
            optional string user_id = 1;
            optional MissionInfo.Spaceship ship = 4;

            message Settings {
                optional bool sfx = 1;
            }
        }

        """;

    private const string Canonical = """
        syntax = "proto2";

        package ei;

        message MissionInfo {
            message Spaceship {
                optional uint32 level = 1;
            }

            optional string id = 1;
        }

        message Backup {
            message Settings {
                optional bool sfx = 1;
                optional bool music = 2;
            }

            optional string user_id = 1;
            optional ei.MissionInfo.Spaceship ship = 4;
        }

        enum Platform {
            UNKNOWN_PLATFORM = 0;
            IOS = 1;
            DROID = 2;
        }

        """;

    [Fact]
    public void Apply_ReorderingAndQualifiersAlone_LeaveMainUntouched() {
        string reordered = Canonical.Replace("        optional bool music = 2;\n", "").Replace("    DROID = 2;\n", "");
        var r = ProtoLayoutPatcher.Apply(Main, reordered);
        Assert.True(r.Ok, r.Error);
        Assert.Equal(Main, r.Text);
    }

    [Fact]
    public void Apply_NewMembers_LandInPlaceWithoutMovingAnythingElse() {
        var r = ProtoLayoutPatcher.Apply(Main, Canonical);
        Assert.True(r.Ok, r.Error);
        string expected = Main
            .Replace("    IOS = 1;\n", "    IOS = 1;\n    DROID = 2;\n")
            .Replace("        optional bool sfx = 1;\n", "        optional bool sfx = 1;\n        optional bool music = 2;\n");
        Assert.Equal(expected, r.Text);
    }
}
