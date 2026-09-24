using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class IntegrityChainTests {
    [Fact]
    public void TargetsText_SortedDistinctAndIncludesGmsAndPackage() {
        string text = IntegrityChain.TargetsText("com.auxbrain.egginc");

        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(lines.Order(StringComparer.Ordinal), lines);
        Assert.Equal(lines.Distinct(StringComparer.Ordinal).Count(), lines.Length);
        Assert.Contains(IntegrityChain.GmsPackage, lines);
        Assert.Contains(IntegrityChain.PlayStorePackage, lines);
        Assert.Contains(IntegrityChain.GsfPackage, lines);
        Assert.Contains("com.auxbrain.egginc", lines);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TargetsText_DuplicatePackage_NotRepeated() {
        string text = IntegrityChain.TargetsText(IntegrityChain.GmsPackage);

        Assert.Equal(1, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(l => l == IntegrityChain.GmsPackage));
    }

    [Fact]
    public void SecurityPatchText_UsesAllKey() =>
        Assert.Equal("all=2026-08-05\n", IntegrityChain.SecurityPatchText("2026-08-05"));

    [Fact]
    public void ApplyCommand_CopiesBeforeTeesimSyncBeforeApplied() {
        string cmd = IntegrityChain.ApplyCommand;

        int prop = cmd.IndexOf("cp -f " + IntegrityChain.StageDir + "/" + IntegrityChain.PifPropFileName + " " + IntegrityChain.PifProp, StringComparison.Ordinal);
        int keybox = cmd.IndexOf("cp -f " + IntegrityChain.StageDir + "/" + IntegrityChain.KeyboxFileName + " " + IntegrityChain.TeesimKeybox, StringComparison.Ordinal);
        int targets = cmd.IndexOf("cp -f " + IntegrityChain.StageDir + "/" + IntegrityChain.TargetsFileName + " " + IntegrityChain.Targets, StringComparison.Ordinal);
        int patch = cmd.IndexOf("cp -f " + IntegrityChain.StageDir + "/" + IntegrityChain.SecurityPatchFileName + " " + IntegrityChain.SecurityPatchFile, StringComparison.Ordinal);
        int teesim = cmd.IndexOf("sh " + IntegrityChain.TeesimSyncScript, StringComparison.Ordinal);
        int applied = cmd.IndexOf("echo applied=1", StringComparison.Ordinal);
        Assert.True(prop >= 0);
        Assert.True(keybox > prop);
        Assert.True(targets > keybox);
        Assert.True(patch > targets);
        Assert.True(teesim > patch);
        Assert.True(applied > teesim);
        Assert.DoesNotContain("'", cmd, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", cmd, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyScript_DifferentModuleDir_AlsoCopiesPropThere() {
        string cmd = IntegrityChain.ApplyScript("/src", "/data/adb/modules_update/playintegrityfix");

        Assert.Contains("cp -f /src/custom.pif.prop /data/adb/modules_update/playintegrityfix/custom.pif.prop", cmd, StringComparison.Ordinal);
        Assert.Contains("sh /data/adb/modules_update/playintegrityfix/webroot/common_scripts/teesim.sh", cmd, StringComparison.Ordinal);
        Assert.Contains("cp -f /src/custom.pif.prop " + IntegrityChain.PifProp, cmd, StringComparison.Ordinal);
    }

    [Fact]
    public void FingerprintCommand_HashesEveryChainFile() {
        Assert.Contains(IntegrityChain.PifProp, IntegrityChain.FingerprintCommand, StringComparison.Ordinal);
        Assert.Contains(IntegrityChain.TeesimKeybox, IntegrityChain.FingerprintCommand, StringComparison.Ordinal);
        Assert.Contains(IntegrityChain.Targets, IntegrityChain.FingerprintCommand, StringComparison.Ordinal);
        Assert.Contains(IntegrityChain.SecurityPatchFile, IntegrityChain.FingerprintCommand, StringComparison.Ordinal);
    }
}
