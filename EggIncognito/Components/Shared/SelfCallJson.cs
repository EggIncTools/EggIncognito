using System.Text.Json;
using EggIncognito.Core.Services;

namespace EggIncognito.Components.Shared;

public static class SelfCallJson {
    public static readonly JsonSerializerOptions Web = JsonPresets.Web;

    public static async Task<List<T>> ListAsync<T>(this HttpClient client, string url, ILogger? log = null,
        CancellationToken ct = default) =>
        (await client.TryListAsync<T>(url, log, ct)).Rows;

    public static async Task<(bool Ok, List<T> Rows)> TryListAsync<T>(this HttpClient client, string url,
        ILogger? log = null, CancellationToken ct = default) {
        try {
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) {
                log?.LogWarning("self call {Url} returned {Status}", url, (int)response.StatusCode);
                return (false, []);
            }

            return (true, await response.Content.ReadFromJsonAsync<List<T>>(Web, ct) ?? []);
        } catch (Exception ex) {
            log?.LogWarning(ex, "self call {Url} failed: {Message}", url, ex.Message);
            return (false, []);
        }
    }

    public static async Task<T?> OneAsync<T>(this HttpClient client, string url, ILogger? log = null,
        CancellationToken ct = default) {
        try {
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) {
                log?.LogDebug("self call {Url} returned {Status}", url, (int)response.StatusCode);
                return default;
            }

            return await response.Content.ReadFromJsonAsync<T>(Web, ct);
        } catch (Exception ex) {
            log?.LogDebug(ex, "self call {Url} failed", url);
            return default;
        }
    }
}
