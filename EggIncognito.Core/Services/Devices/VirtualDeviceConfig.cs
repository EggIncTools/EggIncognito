using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Core.Services.Devices;

public sealed record VirtualDeviceConfig {
    public const string DefaultImage = "redroid/redroid:11.0.0_gapps_ndk_magisk";
    public const string DefaultSocket = "/var/run/docker.sock";

    public const string DefaultGmsPackage = "com.google.android.gms";

    public const string DefaultKeyboxUrl = "https://raw.githubusercontent.com/MeowDump/MeowDump/refs/heads/main/Megatron";

    public static readonly IReadOnlyList<IntegrityModuleSpec> DefaultIntegrityModules = [
        new IntegrityModuleSpec("zygisk", "Dr-TSNG/ZygiskNext", null, "v1.5.0", null, true),
        new IntegrityModuleSpec("tee", "JingMatrix/TEESimulator", null, "v4.0", null, false),
        new IntegrityModuleSpec("integrity-box", "MeowDump/Integrity-Box", null, "v41", null, true)
    ];

    public bool Enabled { get; init; }
    public string Kind { get; init; } = "redroid";
    public string Image { get; init; } = DefaultImage;
    public int MaxInstances { get; init; } = 4;
    public string? Network { get; init; }
    public string DockerSocket { get; init; } = DefaultSocket;
    public int ReconcileSeconds { get; init; } = 20;
    public bool RequireGooglePlay { get; init; } = true;
    public string GmsPackage { get; init; } = DefaultGmsPackage;
    public string? AdbPublicKeyPath { get; init; }
    public string? AdbPublicKey { get; init; }

    public bool IntegrityEnabled { get; init; }
    public int IntegrityRefreshHours { get; init; } = 24;
    public bool IntegrityDisableMagiskZygisk { get; init; } = true;
    public bool IntegrityAllowUnpinned { get; init; }
    public int IntegrityBootTimeoutSeconds { get; init; } = 300;
    public string? IntegrityKeyboxPath { get; init; }
    public string? IntegrityPixelProduct { get; init; }
    public int IntegrityFingerprintRefreshDays { get; init; } = 7;
    public string IntegrityKeyboxUrl { get; init; } = DefaultKeyboxUrl;
    public IReadOnlyList<IntegrityModuleSpec> IntegrityModules { get; init; } = DefaultIntegrityModules;

    public ImageBuildConfig Build { get; init; } = new();

    public static VirtualDeviceConfig Bind(IConfiguration config) {
        var v = config.GetSection("Devices").GetSection("Virtual");
        var integrity = v.GetSection("Integrity");
        return new VirtualDeviceConfig {
            Enabled = Flag(v, "Enabled"),
            Kind = Nz(v["Kind"]) ?? "redroid",
            Image = Nz(v["Image"]) ?? DefaultImage,
            MaxInstances = Num(v, "MaxInstances", 4),
            Network = Nz(v["Network"]),
            DockerSocket = Nz(v["DockerSocket"]) ?? DefaultSocket,
            ReconcileSeconds = Num(v, "ReconcileSeconds", 20),
            RequireGooglePlay = Flag(v, "RequireGooglePlay", true),
            GmsPackage = Nz(v["GmsPackage"]) ?? DefaultGmsPackage,
            AdbPublicKeyPath = Nz(v["AdbPublicKeyPath"]),
            AdbPublicKey = Nz(v["AdbPublicKey"]),
            IntegrityEnabled = Flag(integrity, "Enabled"),
            IntegrityRefreshHours = Num(integrity, "RefreshHours", 24),
            IntegrityDisableMagiskZygisk = Flag(integrity, "DisableMagiskZygisk", true),
            IntegrityAllowUnpinned = Flag(integrity, "AllowUnpinned"),
            IntegrityBootTimeoutSeconds = Num(integrity, "BootTimeoutSeconds", 300),
            IntegrityKeyboxPath = Nz(integrity["KeyboxPath"]),
            IntegrityPixelProduct = Nz(integrity["PixelProduct"]),
            IntegrityFingerprintRefreshDays = Num(integrity, "FingerprintRefreshDays", 7),
            IntegrityKeyboxUrl = Nz(integrity["KeyboxUrl"]) ?? DefaultKeyboxUrl,
            IntegrityModules = Modules(integrity),
            Build = ImageBuildConfig.Bind(v.GetSection("Build"))
        };
    }

    private static IReadOnlyList<IntegrityModuleSpec> Modules(IConfiguration integrity) {
        var specs = new List<IntegrityModuleSpec>();
        foreach (var entry in integrity.GetSection("Modules").GetChildren()) {
            string? name = Nz(entry["Name"]);
            if (name is null) continue;
            specs.Add(new IntegrityModuleSpec(name, Nz(entry["Repo"]), Nz(entry["Url"]),
                Nz(entry["Tag"]), Nz(entry["Sha256"]), Flag(entry, "RebootAfter")));
        }

        return specs.Count > 0 ? specs : DefaultIntegrityModules;
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static bool Flag(IConfiguration config, string key, bool fallback = false) {
        string? raw = Nz(config[key]);
        if (raw is null) return fallback;
        return bool.TryParse(raw, out bool parsed) ? parsed : raw == "1";
    }

    private static int Num(IConfiguration config, string key, int fallback) =>
        int.TryParse(Nz(config[key]), CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
}

public sealed record ImageBuildConfig {
    public const string DefaultGappsUrl =
        "https://sourceforge.net/projects/opengapps/files/x86_64/20220503/open_gapps-x86_64-11.0-pico-20220503.zip";
    public const string DefaultGappsMd5 = "5a6d242be34ad1acf92899c7732afa1b";
    public const string DefaultMagiskUrl = "https://github.com/ayasa520/Magisk/releases/download/v30.7/Magisk-v30.7.apk";
    public const string DefaultMagiskMd5 = "0a31050fdcfaa15f47c9dd1eb8d04fc8";
    public const string DefaultNdkCommit = "9324a8914b649b885dad6f2bfd14a67e5d1520bf";
    public const string DefaultNdkMd5 = "c9572672d1045594448068079b34c350";

    public static string DefaultNdkUrl =>
        "https://github.com/supremegamers/vendor_google_proprietary_ndk_translation-prebuilt/archive/"
        + DefaultNdkCommit + ".zip";

    public bool Enabled { get; init; }
    public string GappsUrl { get; init; } = DefaultGappsUrl;
    public string GappsMd5 { get; init; } = DefaultGappsMd5;
    public string MagiskUrl { get; init; } = DefaultMagiskUrl;
    public string MagiskMd5 { get; init; } = DefaultMagiskMd5;
    public string NdkUrl { get; init; } = DefaultNdkUrl;
    public string NdkMd5 { get; init; } = DefaultNdkMd5;

    public static ImageBuildConfig Bind(IConfiguration build) {
        return new ImageBuildConfig {
            Enabled = Flag(build, "Enabled"),
            GappsUrl = Nz(build["GappsUrl"]) ?? DefaultGappsUrl,
            GappsMd5 = Nz(build["GappsMd5"]) ?? DefaultGappsMd5,
            MagiskUrl = Nz(build["MagiskUrl"]) ?? DefaultMagiskUrl,
            MagiskMd5 = Nz(build["MagiskMd5"]) ?? DefaultMagiskMd5,
            NdkUrl = Nz(build["NdkUrl"]) ?? DefaultNdkUrl,
            NdkMd5 = Nz(build["NdkMd5"]) ?? DefaultNdkMd5
        };
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static bool Flag(IConfiguration config, string key, bool fallback = false) {
        string? raw = Nz(config[key]);
        if (raw is null) return fallback;
        return bool.TryParse(raw, out bool parsed) ? parsed : raw == "1";
    }
}
