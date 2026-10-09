namespace EggIncognito.Models.Devices;

public sealed class DeviceRecertConfig {
    public bool Enabled { get; set; }
    public string KsuWebUiPackage { get; set; } = "io.github.a13e300.ksuwebui";
    public string MagiskPackage { get; set; } = "com.topjohnwu.magisk";
    public string PlayPackage { get; set; } = "com.android.vending";

    public string IntegrityHubLabel { get; set; } = "Integrity Hub";
    public string RepairModeLabel { get; set; } = "Repair Mode";
    public string RepairCompleteText { get; set; } = "repair complete OR check play integrity now";
    public string MagiskModulesLabel { get; set; } = "Modules";
    public string IntegrityBoxLabel { get; set; } = "Integrity box";
    public string MagiskActionLabel { get; set; } = "Action";

    public string? PowerButtonText { get; set; }
    public string? PowerButtonResourceId { get; set; }
    public string? PowerButtonDesc { get; set; }
    public int? PowerButtonX { get; set; }
    public int? PowerButtonY { get; set; }

    public string? MagiskCloseResourceId { get; set; }
    public int? MagiskCloseX { get; set; }
    public int? MagiskCloseY { get; set; }

    public string ExpiryFieldName { get; set; } = "expiry";
    public string? ExpiryFieldResourceId { get; set; }
    public string? ExpiryFieldText { get; set; }
    public string? ExpiryFilePath { get; set; }
    public int ExpiryWarnDays { get; set; } = 3;

    public int RepairTimeoutSeconds { get; set; } = 180;
    public int MagiskActionWaitSeconds { get; set; } = 30;

    public bool VerifyCert { get; set; }
    public string PlayProtectCertifiedText { get; set; } = "Device is certified";
    public string? ProfileDesc { get; set; }
    public string? SettingsLabel { get; set; }
    public string? AboutLabel { get; set; }
}
