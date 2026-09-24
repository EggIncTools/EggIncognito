namespace EggIncognito.Core.Services.Devices;

public sealed record IntegrityChainState(
    bool BoxPresent,
    bool TrickyKeybox,
    bool TeesimKeybox,
    bool TeesimConfig,
    string? Model,
    bool TeesimSynced,
    bool GmsTargeted) {
    public bool Activated => BoxPresent && TeesimKeybox && TeesimConfig && TeesimSynced && GmsTargeted;

    public string Describe() {
        var parts = new List<string> {
            BoxPresent ? "integrity-box present" : "integrity-box missing",
            TrickyKeybox ? "keybox fetched" : "no keybox",
            TeesimKeybox ? "teesim keybox ok" : "teesim has no keybox",
            TeesimConfig ? TeesimSynced ? $"teesim identity {Model}" : "teesim identity unsynced" : "teesim config missing",
            GmsTargeted ? "gms targeted" : "gms not targeted"
        };
        return string.Join(", ", parts);
    }
}

public static class IntegrityChain {
    public const string PifModuleDir = "/data/adb/modules/playintegrityfix";
    public const string ActionScript = PifModuleDir + "/action.sh";
    public const string PifPropFileName = "custom.pif.prop";
    public const string PifProp = PifModuleDir + "/" + PifPropFileName;
    public const string TeesimSyncScriptRelative = "webroot/common_scripts/teesim.sh";
    public const string TeesimSyncScript = PifModuleDir + "/" + TeesimSyncScriptRelative;
    public const string TrickyStoreDir = "/data/adb/tricky_store";
    public const string TrickyKeybox = TrickyStoreDir + "/keybox.xml";
    public const string Targets = TrickyStoreDir + "/target.txt";
    public const string TeesimDir = "/data/adb/teesim";
    public const string TeesimKeybox = TeesimDir + "/keybox.xml";
    public const string TeesimConfig = TeesimDir + "/config.json";
    public const string LogDir = "/data/adb/Box-Brain/Integrity-Box-Logs";
    public const string KeyboxLog = LogDir + "/keybox.log";
    public const string GmsPackage = "com.google.android.gms";
    public const string GsfPackage = "com.google.android.gsf";
    public const string PlayStorePackage = "com.android.vending";
    public const string KeyAttestationPackage = "io.github.vvb2060.keyattestation";
    public const string ScanMarker = "egi-chain-done";
    public const string StageDir = "/data/local/tmp/egi-integrity";
    public const string TargetsFileName = "target.txt";
    public const string KeyboxFileName = "keybox.xml";
    public const string SecurityPatchFileName = "security_patch.txt";
    public const string SecurityPatchFile = TrickyStoreDir + "/" + SecurityPatchFileName;

    public const string FingerprintCommand =
        "sha256sum " + PifProp + " " + TeesimKeybox + " " + Targets + " " + SecurityPatchFile + " 2>/dev/null";

    public const string StateCommand =
        "[ -f " + ActionScript + " ] && echo box=1; "
        + "[ -s " + TrickyKeybox + " ] && echo tskey=1; "
        + "[ -s " + TeesimKeybox + " ] && echo tkey=1; "
        + "[ -f " + TeesimConfig + " ] && echo tcfg=1; "
        + "m=$(grep -m1 ^MODEL= " + PifProp + " 2>/dev/null | cut -d= -f2-); "
        + """[ -n "$m" ] && echo "model=$m"; """
        + """[ -n "$m" ] && grep -qF "$m" """ + TeesimConfig + " 2>/dev/null && echo tsync=1; "
        + "grep -q ^" + GmsPackage + " " + Targets + " 2>/dev/null && echo tgt=1; "
        + "echo " + ScanMarker;

    public static string AdoptKeyboxCommand =>
        "[ -s " + TeesimKeybox + " ] || { [ -s " + TrickyKeybox + " ] && mkdir -p " + TeesimDir
        + " && cp -f " + TrickyKeybox + " " + TeesimKeybox + " && chmod 600 " + TeesimKeybox + "; }; "
        + "[ -s " + TeesimKeybox + " ] && echo adopted=1";

    public static string EnsureTargetCommand(string package) =>
        "mkdir -p " + TrickyStoreDir + "; touch " + Targets + "; "
        + "grep -q ^" + package + " " + Targets + " || echo " + package + " >> " + Targets + "; "
        + "sh " + TeesimSyncScript + " 2>/dev/null; echo targeted=1";

    public static string InstallKeyboxCommand(string staged) =>
        "mkdir -p " + TrickyStoreDir + " " + TeesimDir + "; "
        + "cp -f " + staged + " " + TrickyKeybox + " && cp -f " + staged + " " + TeesimKeybox + " && "
        + "chmod 600 " + TrickyKeybox + " " + TeesimKeybox + " && rm -f " + staged + " && echo keybox=1";

    public const string ReadTeesimKeyboxCommand = "cat " + TeesimKeybox + " 2>/dev/null";

    public static string ResetPlayCommand(bool clearGsf) =>
        "am force-stop " + PlayStorePackage + "; am force-stop " + GmsPackage + "; "
        + "pm clear " + PlayStorePackage + " >/dev/null 2>&1; "
        + (clearGsf ? "pm clear " + GsfPackage + " >/dev/null 2>&1; " : "")
        + "echo reset=1";

    public static string TargetsText(string package) {
        string[] packages = [GmsPackage, PlayStorePackage, GsfPackage, KeyAttestationPackage, package.Trim()];
        return string.Concat(packages
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(p => p + "\n"));
    }

    public static string SecurityPatchText(string patchDate) => $"all={patchDate}\n";

    public static string ApplyScript(string sourceDir, string moduleDir) {
        string prop = sourceDir + "/" + PifPropFileName;
        string keybox = sourceDir + "/" + KeyboxFileName;
        string copies =
            "cp -f " + prop + " " + PifProp
            + (moduleDir == PifModuleDir ? "" : " && cp -f " + prop + " " + moduleDir + "/" + PifPropFileName)
            + " && cp -f " + keybox + " " + TrickyKeybox
            + " && cp -f " + keybox + " " + TeesimKeybox
            + " && chmod 600 " + TrickyKeybox + " " + TeesimKeybox
            + " && cp -f " + sourceDir + "/" + TargetsFileName + " " + Targets
            + " && cp -f " + sourceDir + "/" + SecurityPatchFileName + " " + SecurityPatchFile;
        return "mkdir -p " + PifModuleDir + " " + moduleDir + " " + TrickyStoreDir + " " + TeesimDir + "; "
               + copies + " && { "
               + "sh " + moduleDir + "/" + TeesimSyncScriptRelative + "; "
               + "am force-stop " + GmsPackage + "; am force-stop " + PlayStorePackage + "; "
               + "echo applied=1; }";
    }

    public static string ApplyCommand => ApplyScript(StageDir, PifModuleDir);

    public static bool Ran(string stdout) => stdout.Contains(ScanMarker, StringComparison.Ordinal);

    public static IntegrityChainState Parse(string stdout) {
        var flags = new HashSet<string>(StringComparer.Ordinal);
        string? model = null;
        foreach (string raw in stdout.Split('\n')) {
            string line = raw.Trim();
            if (line.StartsWith("model=", StringComparison.Ordinal)) model = line["model=".Length..].Trim();
            else if (line.EndsWith("=1", StringComparison.Ordinal)) flags.Add(line[..^2]);
        }

        return new IntegrityChainState(
            flags.Contains("box"), flags.Contains("tskey"), flags.Contains("tkey"), flags.Contains("tcfg"),
            model, flags.Contains("tsync"), flags.Contains("tgt"));
    }

    public static IEnumerable<string> ActionLines(string stdout) =>
        stdout.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Select(l => l.Trim())
            .Where(l => l.StartsWith('✦') || l.StartsWith('➤')
                        || l.StartsWith("\U0001236d", StringComparison.Ordinal)
                        || l.Contains("ERROR", StringComparison.Ordinal)
                        || l.Contains("ACTION COMPLETED", StringComparison.Ordinal));
}
