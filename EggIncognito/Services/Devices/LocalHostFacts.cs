using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class LocalHostFacts(
    AdbServerHost adb,
    DeviceProxyPusher pusher,
    IConfiguration configuration) : IHostFacts {
    private const string PemHeader = "-----BEGIN";

    public Task<DeviceResult<HostFacts>> GetAsync(CancellationToken ct) =>
        Task.FromResult(DeviceResult<HostFacts>.Success(new HostFacts(
            Environment.MachineName,
            configuration["GIT_SHA"],
            adb.Socket,
            adb.Owned,
            pusher.HostIp,
            CaptureCaPem())));

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
