using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EggIncognito.Services.Devices;

public static partial class KeyboxRevocation {
    public const string StatusUrl = "https://android.googleapis.com/attestation/status";

    public static List<string> Serials(string keyboxXml) {
        var serials = new List<string>();
        foreach (Match m in PemCertificate().Matches(keyboxXml)) {
            byte[] der;
            try {
                der = Convert.FromBase64String(m.Groups[1].Value.Replace("\r", "").Replace("\n", "").Trim());
            } catch (FormatException) {
                continue;
            }

            try {
                using var cert = X509CertificateLoader.LoadCertificate(der);
                serials.Add(Normalize(cert.SerialNumber));
            } catch (System.Security.Cryptography.CryptographicException) {
            }
        }

        return serials;
    }

    public static async Task<(IReadOnlyList<string> Revoked, string? Error)> RevokedAsync(
        HttpClient http, IReadOnlyList<string> serials, CancellationToken ct) {
        if (serials.Count == 0) return ([], "keybox carries no certificates");
        try {
            using var stream = await http.GetStreamAsync(StatusUrl, ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("entries", out var entries)) return ([], "status list has no entries");
            var revoked = serials
                .Select(s => entries.TryGetProperty(s, out var entry) ? $"{s} ({entry})" : null)
                .OfType<string>()
                .ToList();
            return (revoked, null);
        } catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException) {
            return ([], $"could not fetch the attestation status list: {ex.Message}");
        }
    }

    private static string Normalize(string serialHex) {
        string s = serialHex.ToLowerInvariant().TrimStart('0');
        return s.Length == 0 ? "0" : s;
    }

    [GeneratedRegex(@"-----BEGIN CERTIFICATE-----(.*?)-----END CERTIFICATE-----", RegexOptions.Singleline)]
    private static partial Regex PemCertificate();
}
