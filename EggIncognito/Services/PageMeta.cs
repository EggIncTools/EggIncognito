using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services;

public static class PageMeta {
    public static readonly Meta Default = new(
        "EggIncognito - a toolkit for the Egg, Inc. API",
        "A toolkit for the Egg, Inc. API: a stateless mock server that replays captured responses in " +
        "the exact base64-protobuf wire format, a request inspector, a live capture proxy, a versioned " +
        "proto registry, and a physical device farm. Test tooling without real accounts or rate limits.");

    private static readonly Meta ProtoRegistry = new(
        "EggIncognito - Proto Registry",
        "A versioned registry of Egg, Inc. proto definitions. Drop an .ipa/.apk to extract its schema, " +
        "or browse detected builds per platform across versions.");

    private static readonly (string Prefix, Meta Meta)[] Routes = [
        ("/capture", new Meta(
            "EggIncognito - Capture",
            "A TLS-intercepting proxy that records live Egg, Inc. game traffic into reusable endpoint " +
            "fixtures.")),
        ("/protos", ProtoRegistry),
        ("/protodata", ProtoRegistry),
        ("/docs", new Meta(
            "EggIncognito - Docs",
            "Per-message and per-endpoint documentation for the Egg, Inc. API, with tags and a full proto " +
            "reference.")),
        ("/import", new Meta(
            "EggIncognito - Import",
            "Turn a captured HAR of Egg, Inc. traffic into reusable mock endpoints.")),
        ("/admin", new Meta(
            "EggIncognito - Admin",
            "User roles and shared-store review for the EggIncognito registry."))
    ];

    public static Meta For(string? path) {
        if (string.IsNullOrEmpty(path)) return Default;
        if (ProtoVersion(path) is { } version) return version;
        var best = Default;
        int bestLen = -1;
        foreach ((string prefix, var meta) in Routes) {
            if ((path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                 || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                && prefix.Length > bestLen) {
                best = meta;
                bestLen = prefix.Length;
            }
        }

        return best;
    }

    private static Meta? ProtoVersion(string path) {
        string[] parts = path.Trim('/').Split('/');
        if (parts.Length != 3 || !parts[0].Equals("protos", StringComparison.OrdinalIgnoreCase)) return null;

        string platform = parts[1];
        string build = Uri.UnescapeDataString(parts[2]);
        if (platform.Length is 0 or > 16 || !platform.All(char.IsAsciiLetter)) return null;
        if (build.Length is 0 or > 32 || !build.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            return null;

        string label = PlatformLabel(platform);
        return new Meta(
            $"ei.proto - {label} {build}",
            $"The Egg, Inc. proto schema extracted from {label} build {build}, with a full diff against the " +
            "previous version.");
    }

    private static string PlatformLabel(string platform) =>
        Platforms.Matches(platform, Platforms.Ios) ? "iOS"
        : Platforms.Matches(platform, Platforms.Android) ? "Android"
        : platform;

    public readonly record struct Meta(string Title, string Description);
}
