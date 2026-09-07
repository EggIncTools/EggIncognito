using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public sealed class CaptureCaSource(
    IConfiguration configuration, DeviceTransportConfig transport, IHostFacts hostFacts) {
    public static readonly string RemotePath = Path.Combine(Path.GetTempPath(), "egi-remote-capture-ca.cer");
    private readonly Lock _gate = new();
    private string? _pem;

    public async Task<CaptureCa?> ResolveAsync(CancellationToken ct) {
        if (transport.Mode != DeviceTransportMode.Remote) {
            string path = CaptureCaPath.Resolve(configuration);
            return new CaptureCa(path, CaptureCaPath.AndroidTrustFile(configuration));
        }

        var facts = await hostFacts.GetAsync(ct);
        if (facts.Value?.CaptureCaPem is not { Length: > 0 } pem) return null;
        return Stage(pem);
    }

    private CaptureCa? Stage(string pem) {
        X509Certificate2 cert;
        try {
            cert = X509Certificate2.CreateFromPem(pem);
        } catch (CryptographicException) {
            return null;
        } catch (ArgumentException) {
            return null;
        }

        using (cert) {
            lock (_gate) {
                if (!string.Equals(_pem, pem, StringComparison.Ordinal) || !File.Exists(RemotePath)) {
                    try {
                        File.WriteAllBytes(RemotePath, cert.RawData);
                    } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                        return null;
                    }

                    _pem = pem;
                }
            }

            return new CaptureCa(RemotePath, CaCertPrep.AndroidSubjectHashOld(cert) + ".0");
        }
    }
}
