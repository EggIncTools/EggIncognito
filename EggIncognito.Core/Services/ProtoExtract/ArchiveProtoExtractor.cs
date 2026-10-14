using System.IO.Compression;

namespace EggIncognito.Core.Services.ProtoExtract;

public sealed record ArchiveRules(
    IReadOnlyList<string> NativeLibNames,
    IReadOnlyList<string> PreferredAbis,
    IReadOnlyList<string> SplitPatterns,
    string BaseModuleName,
    IReadOnlyList<string> IosExecutableHints,
    IReadOnlyList<string> PlistNames);

public static class ArchiveProtoExtractor {
    private const long MaxEntryBytes = 300_000_000L;
    private const string NativeLibName = "libegginc.so";
    private const string SharedObjectSuffix = ".so";
    private const string Arm64Abi = "arm64";
    private static readonly string[] FallbackArmAbis = ["armeabi", "armeabi_v7a", "_v7a"];
    private const string ApkSuffix = ".apk";
    private const string ConfigSplitPrefix = "config.";
    private const string BaseModuleName = "base.apk";
    private const string IosPayloadPrefix = "Payload/";
    private const string IosAppDirMarker = ".app/";
    private const string IosFrameworkDirMarker = ".framework/";
    private const string PlistName = "Info.plist";
    private const string AndroidManifestName = "AndroidManifest.xml";

    public static ArchiveRules Rules() => new(
        [NativeLibName],
        [Arm64Abi, .. FallbackArmAbis],
        [ApkSuffix, ConfigSplitPrefix],
        BaseModuleName,
        [IosPayloadPrefix, IosAppDirMarker, IosFrameworkDirMarker],
        [PlistName]);

    public static DescriptorProtoCarver.ExtractResult Extract(byte[] archiveZipBytes) {
        if (archiveZipBytes is null || archiveZipBytes.Length == 0)
            return new DescriptorProtoCarver.ExtractResult(false, null, "empty archive", null, []);

        if (TryReadArmBundle(archiveZipBytes, out byte[] armApk, out byte[]? baseApk)) {
            (string? bv, string? bb) = ReadVersion(baseApk ?? armApk);
            var inner = Extract(armApk);
            return inner.Ok ? inner with { AppVersion = bv, Build = bb } : inner;
        }

        (string? appVersion, string? build) = ReadVersion(archiveZipBytes);

        bool anyCandidate = false;
        foreach (byte[] entryBytes in CandidateBinaries(archiveZipBytes)) {
            anyCandidate = true;
            var r = DescriptorProtoCarver.Extract(entryBytes);
            if (r.Ok) return r with { AppVersion = appVersion, Build = build };
        }

        var raw = DescriptorProtoCarver.Extract(archiveZipBytes);
        if (raw.Ok) return raw with { AppVersion = appVersion, Build = build };
        return raw with {
            Diagnostics = anyCandidate ? raw.Diagnostics : NoBinaryDiagnostics(archiveZipBytes, raw.Diagnostics),
            AppVersion = appVersion,
            Build = build,
        };
    }

    public readonly record struct NativeBinary(byte[] Bin, string? AppVersion, string? Build);

    public static NativeBinary? ReadNativeBinary(byte[] archiveBytes) {
        if (archiveBytes is null || archiveBytes.Length == 0) return null;
        if (!IsZip(archiveBytes)) return new NativeBinary(archiveBytes, null, null);

        if (TryReadArmBundle(archiveBytes, out byte[] armApk, out byte[]? baseApk)) {
            (string? bv, string? bb) = ReadVersion(baseApk ?? armApk);
            return ReadNativeBinary(armApk) is { } inner ? inner with { AppVersion = bv, Build = bb } : null;
        }

        (string? appVersion, string? build) = ReadVersion(archiveBytes);
        byte[]? first = CandidateBinaries(archiveBytes).FirstOrDefault();
        return first is null ? null : new NativeBinary(first, appVersion, build);
    }

    private static string NoBinaryDiagnostics(byte[] zipBytes, string fallback) {
        if (!IsZip(zipBytes)) return fallback;
        byte[]? manifest = ReadAndroidManifest(zipBytes);
        string? splitTypes = manifest is null ? null : ApkVersionCode.ReadStringAttr(manifest, "requiredSplitTypes");
        if (!string.IsNullOrEmpty(splitTypes)) {
            return $"base module of a split install (requiredSplitTypes={splitTypes}): the native library ships in "
                   + "the config.arm64_v8a split, so there is no ei.proto descriptor here; upload the full bundle "
                   + "(.apkm/.xapk) or the arm64 config split apk";
        }

        return "no native library in the archive (expected lib/<abi>/libegginc.so) and no ei.proto descriptor in any entry";
    }

    public static bool IsZip(byte[] b) =>
        b.Length > 4 && b[0] == 0x50 && b[1] == 0x4B && b[2] == 0x03 && b[3] == 0x04;

    private static byte[]? ReadAndroidManifest(byte[] zipBytes) {
        try {
            using var ms = new MemoryStream(zipBytes, false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var manifest = zip.GetEntry(AndroidManifestName);
            return manifest is null ? null : ReadEntry(manifest);
        } catch {
            return null;
        }
    }

    public static bool TryReadArmBundle(byte[] zipBytes, out byte[] armApk, out byte[]? baseApk) {
        armApk = [];
        baseApk = null;
        try {
            using var ms = new MemoryStream(zipBytes, false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var arm = FindArmSplit(zip);
            if (arm is null) return false;

            armApk = ReadEntry(arm);
            var baseEntry = FindBaseModule(zip);
            baseApk = baseEntry is null ? null : ReadEntry(baseEntry);
            return armApk.Length > 0;
        } catch {
            return false;
        }
    }

    public static ZipArchiveEntry? FindArmSplit(ZipArchive zip) =>
        zip.Entries.FirstOrDefault(e => IsApk(e) && e.Name.Contains(Arm64Abi, StringComparison.OrdinalIgnoreCase))
        ?? zip.Entries.FirstOrDefault(e => IsApk(e) && FallbackArmAbis.Any(a => e.Name.Contains(a, StringComparison.OrdinalIgnoreCase)));

    public static ZipArchiveEntry? FindBaseModule(ZipArchive zip) =>
        zip.Entries.FirstOrDefault(e =>
            e.Name.Equals(BaseModuleName, StringComparison.OrdinalIgnoreCase)
            || e.FullName.EndsWith("/" + BaseModuleName, StringComparison.OrdinalIgnoreCase))
        ?? zip.Entries
            .Where(e => IsApk(e) && !e.Name.Contains(ConfigSplitPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Length)
            .FirstOrDefault();

    public static bool IsApk(ZipArchiveEntry e) => e.Name.EndsWith(ApkSuffix, StringComparison.OrdinalIgnoreCase);

    public static ZipArchiveEntry? FindInfoPlist(ZipArchive zip) =>
        zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith(IosPayloadPrefix, StringComparison.OrdinalIgnoreCase)
            && e.FullName.EndsWith(IosAppDirMarker + PlistName, StringComparison.OrdinalIgnoreCase));

    private static byte[] ReadEntry(ZipArchiveEntry entry) {
        using var es = entry.Open();
        using var buf = new MemoryStream();
        es.CopyTo(buf);
        return buf.ToArray();
    }

    private static (string? AppVersion, string? Build) ReadVersion(byte[] zipBytes) {
        try {
            using var ms = new MemoryStream(zipBytes, false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

            var plist = FindInfoPlist(zip);
            if (plist is not null) return AppMetaReader.Read(ReadEntry(plist));

            var manifest = zip.GetEntry(AndroidManifestName);
            if (manifest is not null) return AppMetaReader.Read(ReadEntry(manifest));
        } catch {
            /* metadata is best-effort; extraction does not depend on it */
        }

        return (null, null);
    }

    private static IEnumerable<byte[]> CandidateBinaries(byte[] archiveZipBytes) {
        ZipArchive zip;
        try {
            zip = new ZipArchive(new MemoryStream(archiveZipBytes, false), ZipArchiveMode.Read);
            _ = zip.Entries.Count;
        } catch {
            yield break;
        }

        using (zip) {
            foreach (var entry in OrderedCandidates(zip)) {
                if (entry.Length is <= 0 or > MaxEntryBytes) continue;
                byte[] bytes;
                try {
                    using var es = entry.Open();
                    using var buf = new MemoryStream();
                    es.CopyTo(buf);
                    bytes = buf.ToArray();
                } catch {
                    continue;
                }

                yield return bytes;
            }
        }
    }

    public static bool IsArm64NativeLib(ZipArchiveEntry e) =>
        IsNativeLib(e) && e.FullName.Contains(Arm64Abi, StringComparison.OrdinalIgnoreCase);

    public static bool IsNativeLib(ZipArchiveEntry e) =>
        e.FullName.EndsWith("/" + NativeLibName, StringComparison.OrdinalIgnoreCase);

    public static bool IsArm64SharedObject(ZipArchiveEntry e) =>
        IsAnySharedObject(e) && e.FullName.Contains(Arm64Abi, StringComparison.OrdinalIgnoreCase);

    public static bool IsAnySharedObject(ZipArchiveEntry e) =>
        e.FullName.EndsWith(SharedObjectSuffix, StringComparison.OrdinalIgnoreCase);

    public static bool IsIosAppExecutable(ZipArchiveEntry e) {
        string f = e.FullName;
        if (!f.StartsWith(IosPayloadPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        int appIdx = f.IndexOf(IosAppDirMarker, StringComparison.OrdinalIgnoreCase);
        if (appIdx < 0) return false;
        string rest = f[(appIdx + IosAppDirMarker.Length)..];
        return rest.Length > 0 && !rest.Contains('/') && !rest.Contains('.');
    }

    public static bool IsIosFrameworkBinary(ZipArchiveEntry e) {
        string f = e.FullName;
        return f.StartsWith(IosPayloadPrefix, StringComparison.OrdinalIgnoreCase)
               && f.Contains(IosFrameworkDirMarker, StringComparison.OrdinalIgnoreCase)
               && !f.EndsWith('/') && !e.Name.Contains('.');
    }

    public static IEnumerable<ZipArchiveEntry> OrderedCandidates(ZipArchive zip) {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pred in new Func<ZipArchiveEntry, bool>[] {
                     IsArm64NativeLib, IsNativeLib, IsArm64SharedObject, IsAnySharedObject, IsIosAppExecutable, IsIosFrameworkBinary }) {
            foreach (var e in zip.Entries) {
                if (pred(e) && seen.Add(e.FullName))
                    yield return e;
            }
        }
    }
}
