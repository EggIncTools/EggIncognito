using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Tests.Devices;

public class CaptureCaSourceTests {
    private static X509Certificate2 MakeCa() {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=EggIncognito Capture CA, O=EggIncognito", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
    }

    private static IConfiguration Config(string? caPath) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["CaPath"] = caPath })
        .Build();

    private static DeviceTransportConfig Mode(DeviceTransportMode mode) => new() {
        Mode = mode,
        RemoteBaseUrl = "https://host.test",
        ApiKey = "secret"
    };

    [Fact]
    public async Task ResolveAsync_Remote_StagesThePemAndComputesTheTrustFile() {
        using var cert = MakeCa();
        string pem = CaCertPrep.ToPem(cert);
        var source = new CaptureCaSource(Config(null), Mode(DeviceTransportMode.Remote), new StubHostFacts(pem));

        var ca = await source.ResolveAsync(CancellationToken.None);

        Assert.NotNull(ca);
        Assert.Equal(CaptureCaSource.RemotePath, ca.Path);
        Assert.Equal(CaCertPrep.AndroidSubjectHashOld(cert) + ".0", ca.AndroidTrustFile);
        using var staged = X509CertificateLoader.LoadCertificateFromFile(ca.Path);
        Assert.Equal(cert.Thumbprint, staged.Thumbprint);
    }

    [Fact]
    public async Task ResolveAsync_Remote_NoPem_IsNull() {
        var source = new CaptureCaSource(Config(null), Mode(DeviceTransportMode.Remote), new StubHostFacts(null));

        Assert.Null(await source.ResolveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_Local_PassesTheConfiguredPathThrough() {
        string path = Path.Combine(Path.GetTempPath(), $"egi-ca-local-{Guid.NewGuid():N}.cer");
        using var cert = MakeCa();
        await File.WriteAllBytesAsync(path, cert.Export(X509ContentType.Cert), CancellationToken.None);
        var source = new CaptureCaSource(Config(path), Mode(DeviceTransportMode.Local), new StubHostFacts(null));

        try {
            var ca = await source.ResolveAsync(CancellationToken.None);

            Assert.NotNull(ca);
            Assert.Equal(path, ca.Path);
            Assert.Equal(CaCertPrep.AndroidSubjectHashOld(cert) + ".0", ca.AndroidTrustFile);
        } finally {
            File.Delete(path);
        }
    }

    private sealed class StubHostFacts(string? pem) : IHostFacts {
        public Task<DeviceResult<HostFacts>> GetAsync(CancellationToken ct) =>
            Task.FromResult(DeviceResult<HostFacts>.Success(new HostFacts(
                "host", null, "host", null, "adb", false, true, "192.168.1.66", pem)));
    }
}
