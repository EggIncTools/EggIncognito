namespace EggIncognito.Models.Devices;

public sealed record DeviceReadiness(
    ReadinessCheck Installed,
    ReadinessCheck CaptureCa,
    ReadinessCheck GooglePlay,
    ReadinessCheck Rooted,
    ReadinessCheck IntegrityModule,
    ReadinessCheck Launched,
    ReadinessCheck ProxyReachable);
