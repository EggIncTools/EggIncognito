using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class CaInstallerTests {
    private static (string path, X509Certificate2 cert) MakeCa() {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=EggIncognito Capture CA, O=EggIncognito", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        string path = Path.Combine(Path.GetTempPath(), $"egi-ca-test-{Guid.NewGuid():N}.cer");
        File.WriteAllBytes(path, cert.Export(X509ContentType.Cert));
        return (path, X509CertificateLoader.LoadCertificateFromFile(path));
    }

    [Fact]
    public void AndroidSubjectHashOld_Is8HexDigits_AndStable() {
        (string path, var cert) = MakeCa();
        try {
            string h = CaCertPrep.AndroidSubjectHashOld(cert);
            Assert.Matches("^[0-9a-f]{8}$", h);
            Assert.Equal(h, CaCertPrep.AndroidSubjectHashOld(cert));
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Pem_RoundTrips_ToSameCert() {
        (string path, var cert) = MakeCa();
        try {
            string pem = CaCertPrep.ToPem(cert);
            Assert.StartsWith("-----BEGIN CERTIFICATE-----", pem);
            var back = X509Certificate2.CreateFromPem(pem);
            Assert.Equal(cert.Thumbprint, back.Thumbprint);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void IosHashes_MatchKnownAlgorithms() {
        (string path, var cert) = MakeCa();
        try {
            string sha1 = CaCertPrep.IosCertSha1Hex(cert);
            Assert.Equal(Convert.ToHexString(SHA1.HashData(cert.RawData)).ToLowerInvariant(), sha1);
            string subj = CaCertPrep.IosSubjectDerHex(cert);
            Assert.Equal(Convert.ToHexString(cert.SubjectName.RawData).ToLowerInvariant(), subj);
            string data = CaCertPrep.DerHex(cert);
            Assert.Equal(Convert.ToHexString(cert.RawData).ToLowerInvariant(), data);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Adb_RootShellWithMagiskSu_StillRunsScriptThroughSuMinusMm() {
        (string path, var cert) = MakeCa();
        try {
            var shell = new AdbShell { Uid = "0" };
            var runner = new FakeRunner((_, args) => shell.Handle(args));
            var inst = new AdbCaInstaller(runner);
            (bool ok, string? note) =
                await inst.InstallAsync(new DeviceTarget("d", "android", "SERIAL", "com.auxbrain.egginc"), path, default);

            Assert.True(ok);
            var pushes = runner.Calls.Where(c => c.args.Contains("push")).ToList();
            Assert.Single(pushes);
            Assert.Contains("SERIAL", pushes[0].args);
            string[] args = ScriptRun(runner);
            string[] expectedWrapper = ["/sbin/su", "-mm", "-c"];
            Assert.Equal(expectedWrapper, args[(Array.IndexOf(args, "shell") + 1)..^1]);
            Assert.Contains(CaCertPrep.AndroidSubjectHashOld(cert), note!);
            Assert.Contains("trusted (live)", note!);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Adb_NonRootWithMagiskSu_UsesSuMinusMm() {
        (string path, _) = MakeCa();
        try {
            var shell = new AdbShell { Uid = "2000" };
            var runner = new FakeRunner((_, args) => shell.Handle(args));
            (bool ok, _) = await new AdbCaInstaller(runner)
                .InstallAsync(new DeviceTarget("d", "android", "S", "com.auxbrain.egginc"), path, default);

            Assert.True(ok);
            string[] args = ScriptRun(runner);
            Assert.Contains("/sbin/su", args);
            Assert.Contains("-mm", args);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Adb_SuWithoutMountMaster_ProbesInOrder_AndFallsBackToPlainSu() {
        (string path, _) = MakeCa();
        try {
            var shell = new AdbShell { Uid = "2000", Su = "su", MountMaster = false };
            var runner = new FakeRunner((_, args) => shell.Handle(args));
            (bool ok, _) = await new AdbCaInstaller(runner)
                .InstallAsync(new DeviceTarget("d", "android", "S", "com.auxbrain.egginc"), path, default);

            Assert.True(ok);
            var probes = runner.Calls.Where(c => c.args[^1] == "id")
                .Select(c => string.Join(' ', c.args[(Array.IndexOf(c.args, "shell") + 1)..^2])).ToList();
            string[] expectedOrder = ["/sbin/su -mm", "/sbin/su", "/debug_ramdisk/su -mm", "/debug_ramdisk/su", "su -mm", "su"];
            Assert.Equal(expectedOrder, probes);
            string[] args = ScriptRun(runner);
            Assert.Contains("su", args);
            Assert.DoesNotContain("-mm", args);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Adb_NoSu_RootShell_FallsBackToBareShell() {
        (string path, _) = MakeCa();
        try {
            var shell = new AdbShell { Uid = "0", Su = "" };
            var runner = new FakeRunner((_, args) => shell.Handle(args));
            (bool ok, _) = await new AdbCaInstaller(runner)
                .InstallAsync(new DeviceTarget("d", "android", "S", "com.auxbrain.egginc"), path, default);

            Assert.True(ok);
            string[] args = ScriptRun(runner);
            Assert.DoesNotContain(args, a => a.EndsWith("su", StringComparison.Ordinal));
            Assert.Contains(args, a => a.Contains("sh /data/local/tmp/eggincognito-ca-magisk.sh"));
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Adb_NoSu_NonRootShell_FailsWithPolicyNote_AndSkipsScript() {
        (string path, _) = MakeCa();
        try {
            var shell = new AdbShell { Uid = "2000", Su = "" };
            var runner = new FakeRunner((_, args) => shell.Handle(args));
            (bool ok, string? note) = await new AdbCaInstaller(runner)
                .InstallAsync(new DeviceTarget("d", "android", "S", "com.auxbrain.egginc"), path, default);

            Assert.False(ok);
            Assert.Contains("no working su", note!);
            Assert.Contains("uid 2000", note!);
            Assert.DoesNotContain(runner.Calls, c => c.args.Any(a => a.Contains("sh /data/local/tmp/eggincognito-ca-magisk.sh")));
        } finally {
            File.Delete(path);
        }
    }

    private static string[] ScriptRun(FakeRunner runner) =>
        runner.Calls.Single(c => c.args.Any(a => a.Contains("sh /data/local/tmp/eggincognito-ca-magisk.sh"))).args;

    [Fact]
    public async Task Adb_Script_OverlaysTrustStoreWithTmpfs_AndVerifies() {
        (string path, var cert) = MakeCa();
        try {
            var shell = new AdbShell();
            var runner = new FakeRunner((_, args) => shell.Handle(args));
            await new AdbCaInstaller(runner)
                .InstallAsync(new DeviceTarget("d", "android", "S", "com.auxbrain.egginc"), path, default);

            string script = shell.PushedScript!;
            string hash = CaCertPrep.AndroidSubjectHashOld(cert);
            Assert.Contains($"LIVE=$CACERTS/{hash}.0", script);
            Assert.DoesNotContain("{hash}", script);
            Assert.DoesNotContain("{cert_b64}", script);
            Assert.Contains("mount -t tmpfs", script);
            Assert.Contains("cp -f $CACERTS/* $SNAP/", script);
            Assert.Contains("cp -f $SNAP/* $CACERTS/", script);
            Assert.Contains("chcon u:object_r:system_security_cacerts_file:s0 $CACERTS $CACERTS/*", script);
            Assert.Contains("BEGIN CERTIFICATE", script);
            Assert.DoesNotContain("2>/dev/null", script);
            Assert.DoesNotContain("need su", script);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Adb_Script_GuardsTheTmpfsMountBehindAWritabilityCheck() {
        (string path, _) = MakeCa();
        try {
            var shell = new AdbShell();
            var runner = new FakeRunner((_, args) => shell.Handle(args));
            await new AdbCaInstaller(runner)
                .InstallAsync(new DeviceTarget("d", "android", "S", "com.auxbrain.egginc"), path, default);

            string script = shell.PushedScript!;
            int guard = script.IndexOf("if [ -f $CACERTS/.egi-w ]; then", StringComparison.Ordinal);
            int mount = script.IndexOf("mount -t tmpfs", StringComparison.Ordinal);
            Assert.True(guard >= 0);
            Assert.True(mount > guard);
            Assert.Contains("diag live: store already writable", script);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Adb_LiveMountFails_ReportsTheRealError() {
        (string path, _) = MakeCa();
        try {
            var shell = new AdbShell { Out = LiveFail };
            var runner = new FakeRunner((_, args) => shell.Handle(args));
            (bool ok, string? note) = await new AdbCaInstaller(runner)
                .InstallAsync(new DeviceTarget("d", "android", "S", "com.auxbrain.egginc"), path, default);

            Assert.False(ok);
            Assert.Contains("module written but live mount FAILED", note!);
            Assert.Contains("Permission denied", note!);
            Assert.DoesNotContain("no working su", note!);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Adb_PushFailure_ReturnsFalse_AndSkipsScript() {
        (string path, _) = MakeCa();
        try {
            var runner = new FakeRunner((_, args) =>
                args.Contains("push") ? new ProcessResult(1, "", "no device") : new ProcessResult(0, "", ""));
            var inst = new AdbCaInstaller(runner);
            (bool ok, string? note) = await inst.InstallAsync(new DeviceTarget("d", "android", "S", "com.auxbrain.egginc"), path, default);

            Assert.False(ok);
            Assert.Contains("push", note!);
            Assert.DoesNotContain(runner.Calls, c => c.args.Contains("su"));
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Ios_RunsSqliteInsert_WithBlobsAndStoreSubstituted() {
        (string path, var cert) = MakeCa();
        try {
            var runner = new FakeRunner((_, _) => new ProcessResult(0, "row-present", ""));
            var ssh = new IosCaInstaller.SshConfig("1.2.3.4", "2222", "/k", null, null);
            var inst = new IosCaInstaller(runner, ssh);
            (bool ok, _) = await inst.InstallAsync(new DeviceTarget("d", "ios", "UDID", "com.auxbrain.egginc"), path, default);

            Assert.True(ok);
            (_, string[] args) = runner.Calls.Single(c => c.exe == "ssh");
            string remote = args[^1];
            Assert.Contains(CaCertPrep.IosCertSha256Hex(cert), remote);
            Assert.Contains(CaCertPrep.DerHex(cert), remote);
            Assert.Contains("/private/var/protected/trustd/private/TrustStore.sqlite3", remote);
            Assert.Contains("sha256", remote);
            Assert.Contains("killall -9 trustd", remote);
            Assert.DoesNotContain("{sha256}", remote);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Ios_NoSshCreds_ReturnsFalse() {
        (string path, _) = MakeCa();
        try {
            var runner = new FakeRunner((_, _) => new ProcessResult(0, "", ""));
            var inst = new IosCaInstaller(runner, new IosCaInstaller.SshConfig(null, "2222", null, null, null));
            (bool ok, string? note) = await inst.InstallAsync(new DeviceTarget("d", "ios", "U", "com.auxbrain.egginc"), path, default);
            Assert.False(ok);
            Assert.Contains("ssh", note!);
            Assert.Empty(runner.Calls);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Ios_CustomStorePath_IsUsed() {
        (string path, _) = MakeCa();
        try {
            var runner = new FakeRunner((_, _) => new ProcessResult(0, "", ""));
            var ssh = new IosCaInstaller.SshConfig("h", "22", "/k", null, "/custom/TrustStore.sqlite3");
            var inst = new IosCaInstaller(runner, ssh);
            await inst.InstallAsync(new DeviceTarget("d", "ios", "U", "com.auxbrain.egginc"), path, default);
            string remote = runner.Calls.Single(c => c.exe == "ssh").args[^1];
            Assert.Contains("/custom/TrustStore.sqlite3", remote);
        } finally {
            File.Delete(path);
        }
    }

    private const string LiveOk =
        "diag module: written\ndiag live: tmpfs mounted, 158 certs restored\n"
        + "diag live: mounted into running cacerts\ndiag done";

    private const string LiveFail =
        "diag module: written\ndiag live: tmpfs mount FAILED: mount: Permission denied\n"
        + "diag live: verify FAILED: head: no such file\ndiag done";

    private sealed class AdbShell {
        public string Uid { get; init; } = "0";
        public string Su { get; init; } = "/sbin/su";
        public bool MountMaster { get; init; } = true;
        public string Out { get; init; } = LiveOk;
        public string? PushedScript { get; private set; }

        public ProcessResult Handle(string[] args) {
            if (args.Contains("push")) {
                PushedScript = File.ReadAllText(args[^2]);
                return new ProcessResult(0, "", "");
            }

            string cmd = string.Join(" ", args);
            if (cmd.Contains("id -u")) return new ProcessResult(0, Uid, "");
            if (args[^1] == "id" && args[^2] == "-c") return SuProbe(args[(Array.IndexOf(args, "shell") + 1)..^2]);
            return new ProcessResult(0, Out, "");
        }

        private ProcessResult SuProbe(string[] candidate) {
            bool binaryMatches = Su.Length > 0 && candidate[0] == Su;
            bool flagsAccepted = candidate.Length == 1 || MountMaster;
            return binaryMatches && flagsAccepted
                ? new ProcessResult(0, "uid=0(root) gid=0(root)", "")
                : new ProcessResult(1, "", $"{candidate[0]}: permission denied");
        }
    }
}
