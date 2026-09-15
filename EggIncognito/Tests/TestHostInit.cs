using System.Collections;
using System.Runtime.CompilerServices;

namespace EggIncognito.Tests;

internal static class TestHostInit {
    private static readonly string[] FlatKeys = [
        "ADMIN_API_SECRET",
        "AppMode",
        "CaptureEnabled",
        "CaptureLabel",
        "CaptureOverwrite",
        "CertsPath",
        "ContentRoot",
        "DEPLOY_AGENT_SECRET",
        "DEPLOY_AGENT_URL",
        "EGG_INC_API_SALT",
        "EGG_INC_EID",
        "EndpointsPath",
        "GIT_SHA",
        "HostedCaptureEnabled",
        "HttpPort",
        "HttpsPort",
        "LogsPath",
        "NoBrowser",
        "SHARED_ROLE_ID",
        "TestDbOptIn",
        "WritesEnabled"
    ];

    private static readonly string[] HostKeys = [
        "ASPNETCORE_APPLICATIONNAME",
        "ASPNETCORE_CONTENTROOT",
        "ASPNETCORE_ENVIRONMENT",
        "ASPNETCORE_HTTPS_PORTS",
        "ASPNETCORE_HTTP_PORTS",
        "ASPNETCORE_URLS",
        "DOTNET_ENVIRONMENT"
    ];

    private static readonly string[] Prefixes = ["EGGIDENTITY_"];

    private static readonly string[] Reserved = [
        "ASPNETCORE", "DOTNET", "EGGINCOGNITO_TEST", "MSBUILD", "NUGET", "VSTEST"
    ];

    [ModuleInitializer]
    internal static void Init() {
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables()) {
            if (entry.Key is string name && IsAmbientAppConfig(name)) Environment.SetEnvironmentVariable(name, null);
        }

        Environment.SetEnvironmentVariable("EGGINCOGNITO_TEST_DBFREE", "1");
    }

    internal static bool IsAmbientAppConfig(string name) {
        if (name.Length == 0 || name[0] == '=') return false;
        if (HostKeys.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
        if (Reserved.Any(r => name.StartsWith(r, StringComparison.OrdinalIgnoreCase))) return false;
        if (name.Contains("__", StringComparison.Ordinal) || name.Contains(':', StringComparison.Ordinal)) return true;
        if (Prefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return true;
        return FlatKeys.Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}
