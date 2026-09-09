using System.Net.Http.Headers;
using System.Text.Json;

namespace EggIncognito.FixtureSync;

public static class Program {
    private static readonly (string Id, string Route)[] WireSources = [
        ("get_periodicals", "ei/get_periodicals"),
        ("afx-config", "ei_afx/config"),
        ("season-infos", "ei_ctx/get_season_infos_v2"),
        ("config", "ei/get_config")
    ];

    public static async Task<int> Main(string[] args) {
        if (args.Length < 1) {
            Console.Error.WriteLine("Usage: EggIncognito.FixtureSync <EggIncognito app project directory>");
            return 1;
        }

        string? baseUrl = Environment.GetEnvironmentVariable("EGI_PROD_URL");
        string? apiKey = Environment.GetEnvironmentVariable("EGI_PROD_API_KEY");
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey)) {
            Console.WriteLine("FixtureSync: EGI_PROD_URL or EGI_PROD_API_KEY unset, keeping checked-in fixtures.");
            return 0;
        }

        string defaults = Path.Combine(Path.GetFullPath(args[0]), "Endpoints", "default");
        if (!Directory.Exists(defaults)) {
            Console.Error.WriteLine($"FixtureSync: fixture directory not found: {defaults}");
            return 1;
        }

        using var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        int failed = 0;
        foreach (var (id, route) in WireSources) {
            string target = Path.Combine(defaults, route.Replace('/', Path.DirectorySeparatorChar) + ".json");
            try {
                using var res = await http.GetAsync($"api/v1/data/periodical/{id}");
                if (!res.IsSuccessStatusCode) {
                    Console.Error.WriteLine($"FixtureSync: {id} returned {(int)res.StatusCode}, kept {Path.GetFileName(target)}");
                    failed++;
                    continue;
                }

                byte[] body = await res.Content.ReadAsByteArrayAsync();
                using (JsonDocument.Parse(body)) { }
                if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(body)) {
                    Console.WriteLine($"FixtureSync: {id} unchanged");
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllBytesAsync(target, body);
                Console.WriteLine($"FixtureSync: {id} refreshed ({body.Length} bytes)");
            } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException) {
                Console.Error.WriteLine($"FixtureSync: {id} failed ({ex.Message}), kept {Path.GetFileName(target)}");
                failed++;
            }
        }

        return failed == 0 ? 0 : 2;
    }
}
