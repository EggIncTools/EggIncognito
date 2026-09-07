using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class LocalHostFacts(
    DockerEngineClient docker,
    VirtualDeviceConfig config,
    AdbServerHost adb,
    DeviceProxyPusher pusher,
    IConfiguration configuration) : IHostFacts {
    private const string PemHeader = "-----BEGIN";

    public async Task<DeviceResult<HostFacts>> GetAsync(CancellationToken ct) {
        return DeviceResult<HostFacts>.Success(new HostFacts(
            Environment.MachineName,
            Environment.GetEnvironmentVariable("GIT_SHA"),
            await NetworkAsync(ct),
            AdbHostKey.Resolve(config),
            adb.Socket,
            adb.Owned,
            docker.Available,
            pusher.HostIp,
            CaptureCaPem()));
    }

    private async Task<string?> NetworkAsync(CancellationToken ct) {
        if (config.Network is { Length: > 0 } pinned) return pinned;
        var self = await docker.SelfNetworkAsync(ct);
        return self.Ok ? self.Value : null;
    }

    private string? CaptureCaPem() {
        string path = CaptureCaPath.Resolve(configuration);
        if (!File.Exists(path)) return null;

        try {
            string text = File.ReadAllText(path);
            if (text.StartsWith(PemHeader, StringComparison.Ordinal)) return text;

            using var cert = X509CertificateLoader.LoadCertificateFromFile(path);
            return cert.ExportCertificatePem();
        } catch (Exception ex) when (ex is CryptographicException or IOException) {
            return null;
        }
    }
}
