using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EggIncognito.Core;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;
using EggIncognito.Services.Admin;
using SharpCompress.Compressors.LZMA;

namespace EggIncognito.Services.Devices;

public sealed class ImageBuilder(
    BuildBlobStore blobs,
    ImageBuildStore builds,
    IImageBuildExecutor executor,
    IHttpClientFactory httpFactory,
    VirtualDeviceConfig config,
    IHostFacts hostFacts,
    IntegrityAssets assets,
    CaptureCaSource captureCa,
    AdminNotifier notifier,
    ILogger<ImageBuilder> logger) {
    public const string HttpClientName = "image-build";

    private const UnixFileMode DirMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private const UnixFileMode FileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private const UnixFileMode ExecMode = DirMode;

    private static readonly string[] GappsSkip =
        ["setupwizarddefault-x86_64.tar.lz", "setupwizardtablet-x86_64.tar.lz"];

    private static readonly string[] GappsCommon =
        ["defaultetc-common.tar.lz", "defaultframework-common.tar.lz", "googlepixelconfig-common.tar.lz",
         "vending-common.tar.lz"];

    public async Task<ImageBuildOutcome> BuildAsync(ImageBuildSpec spec, long buildId, CancellationToken ct) {
        string contextDir = Directory.CreateTempSubdirectory("egi-imgbuild-").FullName;
        string tarPath = Path.Combine(Path.GetTempPath(), $"egi-imgctx-{Guid.NewGuid():N}.tar");
        var execPaths = new HashSet<string>(StringComparer.Ordinal);
        try {
            await AdvanceAsync(buildId, ImageBuildStates.Downloading, "resolving components", ct);

            if (spec.Ndk) {
                await Log(buildId, "ndk: downloading + unpacking", ct);
                await BuildNdkAsync(contextDir, buildId, ct);
            }

            if (spec.Gapps) {
                await Log(buildId, "gapps: downloading + unpacking", ct);
                await BuildGappsAsync(contextDir, buildId, ct);
            }

            if (spec.Magisk) {
                await Log(buildId, "magisk: downloading + unpacking", ct);
                await BuildMagiskAsync(contextDir, execPaths, buildId, ct);
            }

            if (spec.Integrity) {
                await Log(buildId, "integrity: resolving identity, keybox and modules", ct);
                await BuildIntegrityAsync(contextDir, execPaths, buildId, ct);
            }

            WriteDockerfile(contextDir, spec);
            await Log(buildId, $"assembling build context for {spec.ResolvedTag}", ct);
            AssembleTar(contextDir, tarPath, execPaths);

            await AdvanceAsync(buildId, ImageBuildStates.Building, "streaming to docker", ct);
            await using var tar = File.OpenRead(tarPath);
            var outcome = await executor.BuildAsync(
                tar, spec.ResolvedTag, null,
                line => builds.AppendAsync(buildId, line, CancellationToken.None).GetAwaiter().GetResult(), ct);

            await SettleAsync(buildId,
                outcome.Ok ? ImageBuildStates.Ready : ImageBuildStates.Failed, outcome.Tag, outcome.Note, ct);
            return outcome;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogError(ex, "image build {Id} for {Tag} failed", buildId, spec.ResolvedTag);
            await SettleAsync(buildId, ImageBuildStates.Failed, spec.ResolvedTag, ex.Message, CancellationToken.None);
            return new ImageBuildOutcome(false, spec.ResolvedTag, ex.Message);
        } finally {
            TryDelete(tarPath);
            TryDeleteDir(contextDir);
        }
    }

    private async Task AdvanceAsync(long buildId, string state, string note, CancellationToken ct) {
        await builds.SetStateAsync(buildId, state, note, ct);
        notifier.Publish(AdminTopics.ImageBuilds);
    }

    private async Task SettleAsync(long buildId, string state, string tag, string? note, CancellationToken ct) {
        await builds.FinishAsync(buildId, state, tag, note, ct);
        notifier.Publish(AdminTopics.ImageBuilds);
    }

    private async Task BuildNdkAsync(string contextDir, long buildId, CancellationToken ct) {
        byte[] zip = await FetchAsync("ndk", config.Build.NdkUrl, config.Build.NdkMd5, "ndk",
            config.Build.NdkUrl, buildId, ct);
        string unpack = FreshDir(Path.Combine(Path.GetTempPath(), $"egi-ndk-{Guid.NewGuid():N}"));
        try {
            ExtractZip(zip, unpack);
            string top = SingleTopDir(unpack) ?? throw new InvalidOperationException("ndk: archive has no top-level dir");
            string prebuilts = Path.Combine(top, "prebuilts");
            if (!Directory.Exists(prebuilts))
                throw new InvalidOperationException($"ndk: no prebuilts tree under {Path.GetFileName(top)}");

            CopyTree(prebuilts, Path.Combine(contextDir, "ndk", "system"));
            await Log(buildId, "ndk: staged prebuilts into ndk/system", ct);
        } finally {
            TryDeleteDir(unpack);
        }
    }

    private async Task BuildGappsAsync(string contextDir, long buildId, CancellationToken ct) {
        byte[] zip = await FetchAsync("gapps", config.Build.GappsUrl, config.Build.GappsMd5, "gapps",
            config.Build.GappsUrl, buildId, ct);
        string outer = FreshDir(Path.Combine(Path.GetTempPath(), $"egi-gapps-{Guid.NewGuid():N}"));
        try {
            ExtractZip(zip, outer);
            string core = Path.Combine(outer, "Core");
            if (!Directory.Exists(core)) throw new InvalidOperationException("gapps: archive has no Core dir");

            string system = Path.Combine(contextDir, "gapps", "system");
            string privApp = Path.Combine(system, "priv-app");
            Directory.CreateDirectory(privApp);

            var payloads = Directory.EnumerateFiles(core, "*.tar.lz")
                .Where(p => !GappsSkip.Contains(Path.GetFileName(p), StringComparer.Ordinal))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
            int done = 0;
            foreach (string lz in payloads) {
                string name = Path.GetFileName(lz);
                await Log(buildId, $"gapps: unpacking {name} ({++done}/{payloads.Count})", ct);

                string appUnpack = FreshDir(Path.Combine(outer, "appunpack"));
                ExtractTarLz(await File.ReadAllBytesAsync(lz, ct), appUnpack);
                string appRoot = SingleTopDir(appUnpack) ?? appUnpack;

                if (GappsCommon.Contains(name, StringComparer.Ordinal)) {
                    string common = Path.Combine(appRoot, "common");
                    if (Directory.Exists(common)) CopyTree(common, system);
                    continue;
                }

                foreach (string priv in Directory.EnumerateDirectories(appRoot, "priv-app", SearchOption.AllDirectories)) {
                    foreach (string appDir in Directory.EnumerateDirectories(priv))
                        CopyTree(appDir, Path.Combine(privApp, Path.GetFileName(appDir)));
                }
            }

            await Log(buildId, "gapps: staged priv-app + framework into gapps/system", ct);
        } finally {
            TryDeleteDir(outer);
        }
    }

    private async Task BuildMagiskAsync(
        string contextDir, HashSet<string> execPaths, long buildId, CancellationToken ct) {
        byte[] apk = await FetchAsync("magisk", config.Build.MagiskUrl, config.Build.MagiskMd5, "magisk",
            config.Build.MagiskUrl, buildId, ct);
        string unpack = FreshDir(Path.Combine(Path.GetTempPath(), $"egi-magisk-{Guid.NewGuid():N}"));
        try {
            ExtractZip(apk, unpack);
            string magiskDir = Path.Combine(contextDir, "magisk", "system", "etc", "init", "magisk");
            Directory.CreateDirectory(magiskDir);
            Directory.CreateDirectory(Path.Combine(contextDir, "magisk", "sbin"));

            string libDir = Path.Combine(unpack, "lib", "x86_64");
            if (!Directory.Exists(libDir)) throw new InvalidOperationException("magisk: no lib/x86_64 in the apk");

            foreach (string so in Directory.EnumerateFiles(libDir, "lib*.so")) {
                string bare = Path.GetFileName(so);
                string applet = bare[3..^3];
                string dest = Path.Combine(magiskDir, applet);
                File.Copy(so, dest, true);
                execPaths.Add(RelKey(contextDir, dest));
            }

            string assetsDir = Path.Combine(unpack, "assets");
            if (!Directory.Exists(assetsDir)) throw new InvalidOperationException("magisk: no assets dir in the apk");

            foreach (string sh in Directory.EnumerateFiles(assetsDir, "*.sh")) {
                string dest = Path.Combine(magiskDir, Path.GetFileName(sh));
                File.Copy(sh, dest, true);
                execPaths.Add(RelKey(contextDir, dest));
            }

            if (!File.Exists(Path.Combine(magiskDir, "util_functions.sh")))
                throw new InvalidOperationException("magisk: apk has no assets/util_functions.sh");

            await File.WriteAllBytesAsync(Path.Combine(magiskDir, "magisk.apk"), apk, ct);

            string bootanim = Path.Combine(contextDir, "magisk", "system", "etc", "init", "bootanim.rc");
            await File.WriteAllTextAsync(bootanim, BootanimRc, new UTF8Encoding(false), ct);
            WriteGzip(BootanimServiceBlock, bootanim + ".gz");

            await Log(buildId, "magisk: staged applets + shell assets + bootanim.rc hook", ct);
        } finally {
            TryDeleteDir(unpack);
        }
    }

    private async Task BuildIntegrityAsync(
        string contextDir, HashSet<string> execPaths, long buildId, CancellationToken ct) {
        var bundle = await assets.ResolveAsync(false, ct);
        if (!bundle.Ok || bundle.Profile is not { } profile)
            throw new InvalidOperationException("integrity: " + (bundle.Error ?? "assets did not resolve"));

        foreach (var module in bundle.Modules)
            await Log(buildId, $"integrity: module {module.Spec.Name} [{module.ModuleId}] {module.Version ?? "?"}, {module.Zip.Length} bytes", ct);
        await Log(buildId,
            $"integrity: identity {profile.Model} {profile.Fingerprint}, expires {profile.Expiry?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown"}", ct);
        await Log(buildId, $"integrity: keybox {bundle.KeyboxSource}, {bundle.KeyboxSerials.Count} certs, {bundle.KeyboxNote}", ct);
        foreach (string warning in bundle.Warnings) await Log(buildId, "integrity: " + warning, ct);

        var resolvedKey = await HostAdbKey.ResolveAsync(hostFacts, config, ct);
        string? adbKey = resolvedKey?.Key;
        await Log(buildId, resolvedKey is { } rk
            ? $"integrity: host adb public key {AdbHostKey.Label(rk.Key)} from {rk.Source} baked as {IntegritySeed.RootAdbKeysFile} and into the seed; "
              + "it must be the key of the adb server this app talks to, so an adb server started elsewhere rejects it"
            : "integrity: no host adb public key found; the image carries none and adbd will reject this host after the seed boot", ct);

        (string? caHash, string? caPem) = await CaptureCaAsync(ct);
        await Log(buildId, caHash is null
            ? "integrity: no capture CA minted yet, the image trusts none"
            : $"integrity: capture CA baked as {caHash}.0", ct);

        string root = Path.Combine(contextDir, "integrity");
        foreach (var file in IntegrityLayout.Plan(bundle, adbKey, caHash, caPem)) {
            string dest = Path.Combine(root, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            await File.WriteAllBytesAsync(dest, file.Bytes, ct);
            if (file.Exec) execPaths.Add(RelKey(contextDir, dest));
        }

        await Log(buildId, $"integrity: staged {bundle.Modules.Count} module(s) + seed under {IntegritySeed.SeedDir}", ct);
    }

    private async Task<(string? Hash, string? Pem)> CaptureCaAsync(CancellationToken ct) {
        var ca = await captureCa.ResolveAsync(ct);
        if (ca is null || !File.Exists(ca.Path)) return (null, null);
        try {
            using var cert = X509CertificateLoader.LoadCertificateFromFile(ca.Path);
            return (CaCertPrep.AndroidSubjectHashOld(cert), CaCertPrep.ToPem(cert));
        } catch (CryptographicException ex) {
            logger.LogWarning(ex, "image build: capture CA at {Path} is unreadable", ca.Path);
            return (null, null);
        }
    }

    private static void WriteDockerfile(string contextDir, ImageBuildSpec spec) {
        var sb = new StringBuilder();
        sb.Append("FROM ").Append(spec.ResolvedBaseImage).Append('\n');
        if (spec.Gapps) sb.Append("COPY gapps /\n");
        if (spec.Ndk) sb.Append("COPY ndk /\n");
        if (spec.Magisk) sb.Append("COPY magisk /\n");
        if (spec.Integrity) sb.Append("COPY integrity /\n");
        File.WriteAllText(Path.Combine(contextDir, "Dockerfile"), sb.ToString(), new UTF8Encoding(false));
    }

    private static void AssembleTar(string contextDir, string tarPath, HashSet<string> execPaths) {
        using var file = File.Create(tarPath);
        using var writer = new TarWriter(file, TarEntryFormat.Pax, false);

        foreach (string dir in Directory.EnumerateDirectories(contextDir, "*", SearchOption.AllDirectories)
                     .OrderBy(d => d, StringComparer.Ordinal)) {
            string rel = RelKey(contextDir, dir);
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, rel + "/") { Mode = DirMode });
        }

        foreach (string path in Directory.EnumerateFiles(contextDir, "*", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.Ordinal)) {
            string rel = RelKey(contextDir, path);
            var mode = FileMode;
            if (execPaths.Contains(rel)) mode = ExecMode;
            else if (rel.StartsWith("ndk/", StringComparison.Ordinal) && !rel.EndsWith(".rc", StringComparison.Ordinal))
                mode = ExecMode;

            var entry = new PaxTarEntry(TarEntryType.RegularFile, rel) { Mode = mode };
            using var src = File.OpenRead(path);
            entry.DataStream = src;
            writer.WriteEntry(entry);
        }
    }

    private async Task<byte[]> FetchAsync(
        string component, string url, string expectedMd5, string label, string pin, long buildId, CancellationToken ct) {
        string key = $"{component}:{expectedMd5}";
        var cached = await blobs.GetAsync(key, ct);
        try {
            var http = httpFactory.CreateClient(HttpClientName);
            await Log(buildId, $"{label}: downloading {url}", ct);
            byte[] bytes = await http.GetByteArrayAsync(url, ct);
            string md5 = Md5Hex(bytes);
            if (!string.IsNullOrEmpty(expectedMd5) && !string.Equals(md5, expectedMd5, StringComparison.OrdinalIgnoreCase)) {
                if (cached is not null) {
                    await Log(buildId, $"{label}: md5 mismatch on download (got {md5}); using cached blob", ct);
                    return cached.Bytes;
                }

                throw new InvalidOperationException(
                    $"{label}: md5 mismatch (got {md5}, pinned {expectedMd5}); check the pin {pin}");
            }

            await blobs.PutAsync(key, url, Hashes.Sha256Hex(bytes), bytes, ct);
            await Log(buildId, $"{label}: downloaded {bytes.LongLength} bytes", ct);
            return bytes;
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) {
            if (cached is not null) {
                await Log(buildId, $"{label}: download failed ({ex.Message}); using cached blob", ct);
                return cached.Bytes;
            }

            throw new InvalidOperationException(
                $"{label}: download failed for pin {pin} and no cached blob is present: {ex.Message}", ex);
        }
    }

    private Task Log(long buildId, string line, CancellationToken ct) {
        logger.LogInformation("image build {Id}: {Line}", buildId, line);
        return builds.AppendAsync(buildId, line, ct);
    }

    private static void ExtractZip(byte[] bytes, string destDir) {
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries) {
            string dest = SafeCombine(destDir, entry.FullName);
            if (entry.FullName.EndsWith('/')) {
                Directory.CreateDirectory(dest);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, true);
        }
    }

    private static void ExtractTarLz(byte[] lzBytes, string destDir) {
        using var ms = new MemoryStream(DecodeLzip(lzBytes), false);
        using var reader = new TarReader(ms);
        while (reader.GetNextEntry() is { } entry) {
            string rel = entry.Name.Replace('/', Path.DirectorySeparatorChar);
            if (rel.Length == 0) continue;
            string dest = SafeCombine(destDir, entry.Name);
            if (entry.EntryType is TarEntryType.Directory) {
                Directory.CreateDirectory(dest);
                continue;
            }

            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, true);
        }
    }

    private static byte[] DecodeLzip(byte[] lz) {
        using var outMs = new MemoryStream();
        var members = LzipMembers(lz);
        for (int i = 0; i < members.Count; i++) {
            (int start, int len) = members[i];
            using var member = new MemoryStream(lz, start, len, false);
            using var dec = LZipStream.Create(member, SharpCompress.Compressors.CompressionMode.Decompress);
            try {
                dec.CopyTo(outMs);
            } catch (Exception ex) {
                throw new InvalidOperationException(
                    $"lzip: decode failed on member {i + 1}/{members.Count} (offset {start}, {len} bytes): {ex.Message}", ex);
            }
        }

        return outMs.ToArray();
    }

    private static List<(int Start, int Len)> LzipMembers(byte[] lz) {
        var members = new List<(int, int)>();
        long end = lz.Length;
        while (end > 20) {
            long size = BitConverter.ToInt64(lz, checked((int)(end - 8)));
            long start = end - size;
            if (size <= 20 || start < 0)
                throw new InvalidOperationException($"lzip: bad member size {size} at offset {end - 8}");
            if (lz[start] != 0x4C || lz[start + 1] != 0x5A || lz[start + 2] != 0x49 || lz[start + 3] != 0x50)
                throw new InvalidOperationException($"lzip: member magic mismatch at offset {start}");

            members.Add((checked((int)start), checked((int)size)));
            end = start;
        }

        members.Reverse();
        return members;
    }

    private static void CopyTree(string src, string dest) {
        Directory.CreateDirectory(dest);
        foreach (string dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(src, dir)));
        foreach (string file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)) {
            string target = Path.Combine(dest, Path.GetRelativePath(src, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static string? SingleTopDir(string dir) {
        var dirs = Directory.GetDirectories(dir);
        var files = Directory.GetFiles(dir);
        if (dirs.Length == 1 && files.Length == 0) return dirs[0];
        return dirs.Length > 0 ? dirs[0] : null;
    }

    private static string SafeCombine(string root, string entryPath) {
        string full = Path.GetFullPath(Path.Combine(root, entryPath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal) && full != Path.GetFullPath(root))
            throw new InvalidOperationException($"archive entry escapes the extract dir: {entryPath}");
        return full;
    }

    private static string RelKey(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string FreshDir(string path) {
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteGzip(string text, string path) {
        using var file = File.Create(path);
        using var gz = new GZipStream(file, CompressionLevel.Optimal);
        gz.Write(Encoding.UTF8.GetBytes(text));
    }

    private static string Md5Hex(byte[] bytes) {
#pragma warning disable CA5351
        return Convert.ToHexStringLower(MD5.HashData(bytes));
#pragma warning restore CA5351
    }

    private void TryDelete(string path) {
        try {
            if (File.Exists(path)) File.Delete(path);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            logger.LogDebug(ex, "image build: could not delete temp file {Path}", path);
        }
    }

    private void TryDeleteDir(string path) {
        try {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            logger.LogDebug(ex, "image build: could not delete temp dir {Path}", path);
        }
    }

    private const string BootanimServiceBlock =
        "service bootanim /system/bin/bootanimation\n"
        + "    class core animation\n"
        + "    user graphics\n"
        + "    group graphics audio\n"
        + "    disabled\n"
        + "    oneshot\n"
        + "    ioprio rt 0\n"
        + "    task_profiles MaxPerformance\n";

    private const string BootanimRc = BootanimServiceBlock
        + "on post-fs-data\n"
        + "    start logd\n"
        + "    exec u:r:su:s0 root root -- /system/etc/init/magisk/magiskpolicy --live --magisk\n"
        + "    exec u:r:magisk:s0 root root -- /system/etc/init/magisk/magiskpolicy --live --magisk\n"
        + "    exec u:r:update_engine:s0 root root -- /system/etc/init/magisk/magiskpolicy --live --magisk\n"
        + "    exec u:r:su:s0 root root -- /system/etc/init/magisk/magisk --auto-selinux --setup-sbin /system/etc/init/magisk /sbin\n"
        + "    exec u:r:su:s0 root root -- /sbin/magisk --auto-selinux --post-fs-data\n"
        + "on nonencrypted\n"
        + "    exec u:r:su:s0 root root -- /sbin/magisk --auto-selinux --service\n"
        + "on property:vold.decrypt=trigger_restart_framework\n"
        + "    exec u:r:su:s0 root root -- /sbin/magisk --auto-selinux --service\n"
        + "on property:sys.boot_completed=1\n"
        + "    mkdir /data/adb/magisk 755\n"
        + "    exec u:r:su:s0 root root -- /system/bin/sh -c \"cp -f /system/etc/init/magisk/* /data/adb/magisk/ ; rm -f /data/adb/magisk/magisk.apk ; chmod 755 /data/adb/magisk/* ; chcon u:object_r:magisk_file:s0 /data/adb/magisk /data/adb/magisk/*\"\n"
        + "    exec u:r:su:s0 root root -- /sbin/magisk --auto-selinux --boot-complete\n"
        + "    exec -- /system/bin/sh -c \"if [ ! -e /data/data/io.github.huskydg.magisk ] ; then pm install /system/etc/init/magisk/magisk.apk ; fi\"\n"
        + "on property:init.svc.zygote=restarting\n"
        + "    exec u:r:su:s0 root root -- /sbin/magisk --auto-selinux --zygote-restart\n"
        + "on property:init.svc.zygote=stopped\n"
        + "    exec u:r:su:s0 root root -- /sbin/magisk --auto-selinux --zygote-restart\n";
}
