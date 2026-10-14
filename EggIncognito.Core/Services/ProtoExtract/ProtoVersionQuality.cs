using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class ProtoVersionQuality {
    public static bool IsAndroidStyleBuild(string? build) =>
        !string.IsNullOrWhiteSpace(build) && build.All(char.IsDigit);

    public static bool HasPlatformBuildMismatch(string? platform, string? build) {
        if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(build)) return false;
        bool isIos = Platforms.Matches(platform, Platforms.Ios);
        return isIos && IsAndroidStyleBuild(build);
    }

    public static string? BuildQualityFlag(string? platform, string? build) =>
        HasPlatformBuildMismatch(platform, build) ? "android_build_on_ios" : null;

    public const string FlawNoClientVersion = "no_client_version";
    public const string FlawBuildPlatformMismatch = "build_platform_mismatch";
    public const string FlawNoProto = "no_proto";

    public static IReadOnlyList<string> Flaws(
        string? platform, string? build, string? clientVersion,
        string? protoSha, bool hasProtoText) {
        var flaws = new List<string>();
        if (string.IsNullOrWhiteSpace(clientVersion)) flaws.Add(FlawNoClientVersion);
        if (HasPlatformBuildMismatch(platform, build)) flaws.Add(FlawBuildPlatformMismatch);
        if (!hasProtoText || string.IsNullOrWhiteSpace(protoSha)) flaws.Add(FlawNoProto);
        return flaws;
    }
}
