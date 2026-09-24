using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class IntegritySeedTests {
    private static readonly string[] Modules = ["zygisksu.zip", "teesim.zip", "playintegrityfix.zip"];

    [Fact]
    public void Script_DisablesZygiskBeforeInstallingModules() {
        string script = IntegritySeed.Script(Modules);

        int zygisk = script.IndexOf("VALUES('zygisk',0)", StringComparison.Ordinal);
        int install = script.IndexOf("\"$MAGISK\" --install-module ", StringComparison.Ordinal);
        Assert.True(zygisk >= 0);
        Assert.True(install > zygisk);
    }

    [Fact]
    public void Script_InstallsModulesInOrder() {
        string script = IntegritySeed.Script(Modules);

        int first = script.IndexOf("--install-module " + IntegritySeed.ModulesDir + "/zygisksu.zip", StringComparison.Ordinal);
        int second = script.IndexOf("--install-module " + IntegritySeed.ModulesDir + "/teesim.zip", StringComparison.Ordinal);
        int third = script.IndexOf("--install-module " + IntegritySeed.ModulesDir + "/playintegrityfix.zip", StringComparison.Ordinal);
        Assert.True(first >= 0);
        Assert.True(second > first);
        Assert.True(third > second);
    }

    [Fact]
    public void Script_WritesStateBeforeAnyWait_AndLogsReadably() {
        string script = IntegritySeed.Script(Modules);

        int installing = script.IndexOf("echo " + IntegritySeed.StateInstalling + " > " + IntegritySeed.StateFile, StringComparison.Ordinal);
        int bootWait = script.IndexOf("sys.boot_completed", StringComparison.Ordinal);
        Assert.True(installing >= 0);
        Assert.True(bootWait > installing);
        Assert.Contains("echo " + IntegritySeed.StateDone + " > " + IntegritySeed.StateFile, script, StringComparison.Ordinal);
        Assert.Contains("echo " + IntegritySeed.StateFailed + " > " + IntegritySeed.StateFile, script, StringComparison.Ordinal);
        Assert.Contains("chmod 644 " + IntegritySeed.LogFile, script, StringComparison.Ordinal);
        Assert.Contains("touch " + IntegritySeed.Marker + "\n", script, StringComparison.Ordinal);
        Assert.StartsWith("/data/local/tmp/", IntegritySeed.LogFile, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_StartsWithShebangAndEndsWithReboot() {
        string script = IntegritySeed.Script(Modules);

        Assert.StartsWith("#!/system/bin/sh\n", script, StringComparison.Ordinal);
        Assert.EndsWith("/system/bin/reboot\n", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_AppliesChainFromSeedDirIntoPendingModule() {
        string script = IntegritySeed.Script(Modules);

        Assert.Contains(IntegrityChain.ApplyScript(IntegritySeed.SeedDir, IntegritySeed.PendingModuleDir), script, StringComparison.Ordinal);
        Assert.Contains("pm clear " + IntegrityChain.PlayStorePackage, script, StringComparison.Ordinal);
        Assert.Contains("pm clear " + IntegrityChain.GsfPackage, script, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_SeedsMagiskBinDirWhenTheBaseHookHasNot() {
        string script = IntegritySeed.Script(Modules);

        int seed = script.IndexOf("cp -f /system/etc/init/magisk/* /data/adb/magisk/", StringComparison.Ordinal);
        int install = script.IndexOf("\"$MAGISK\" --install-module ", StringComparison.Ordinal);
        Assert.True(seed >= 0);
        Assert.True(install > seed);
    }

    [Fact]
    public void Rc_DefinesNamedServiceStartedOnBoot() {
        Assert.StartsWith("service " + IntegritySeed.ServiceName + " /system/bin/sh " + IntegritySeed.SeedScript + "\n", IntegritySeed.Rc, StringComparison.Ordinal);
        Assert.Contains("    seclabel u:r:su:s0\n", IntegritySeed.Rc, StringComparison.Ordinal);
        Assert.Contains("    oneshot\n", IntegritySeed.Rc, StringComparison.Ordinal);
        Assert.Contains("on boot\n    start " + IntegritySeed.ServiceName + "\n", IntegritySeed.Rc, StringComparison.Ordinal);
        Assert.Equal("init.svc.egi-seed", IntegritySeed.ServiceProp);
    }

    [Fact]
    public void Rc_AlsoStartsTheServiceOnBootCompleted() =>
        Assert.Contains("on property:sys.boot_completed=1\n    start " + IntegritySeed.ServiceName + "\n", IntegritySeed.Rc, StringComparison.Ordinal);

    [Fact]
    public void Parse_ReadsStateImageMarkerServiceAndLastLogLine() {
        Assert.Equal(new SeedProbe(true, true, "installing", "running", "step: installing 02-tee.zip"),
            IntegritySeed.Parse("installing\nseeded-image\nsvc=running\nlog=step: installing 02-tee.zip\n"));
        Assert.Equal(new SeedProbe(true, true, "done", "stopped", null),
            IntegritySeed.Parse("done\r\nseeded-image\r\nsvc=stopped\r\nlog=\r\n"));
        Assert.Equal(new SeedProbe(true, false, "failed", null, null), IntegritySeed.Parse("failed\nsvc=\nlog=\n"));
    }

    [Fact]
    public void Parse_FlagsAProbeThatNeverRan() {
        Assert.Equal(new SeedProbe(false, false, null, null, null), IntegritySeed.Parse(""));
        Assert.Equal(new SeedProbe(false, false, null, null, null), IntegritySeed.Parse("error: device offline\n"));
    }
}
