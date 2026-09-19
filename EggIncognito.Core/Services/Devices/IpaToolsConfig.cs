namespace EggIncognito.Core.Services.Devices;

public sealed class IpaToolsConfig {
    public string IpaToolPath { get; set; } = "/var/run/ipatool";
    public string IpaDecryptPath { get; set; } = "/var/run/ipadecrypt";
    public string BundleId { get; set; } = "com.auxbrain.egginc";
    public int TimeoutSeconds { get; set; } = 120;

    public bool Enabled => !string.IsNullOrWhiteSpace(IpaToolPath) && File.Exists(IpaToolPath);
}
