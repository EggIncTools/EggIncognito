using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EggIdentity.Contract;

namespace EggIncognito.Runner.Posting;

public sealed class EventPoster(HttpClient http, string url, string secret) {
    public async Task PostAsync(NewVersionEvent evt) {
        var json = JsonSerializer.Serialize(evt);
        using var req = new HttpRequestMessage(HttpMethod.Post, url) {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        var resp = await http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
    }
}
