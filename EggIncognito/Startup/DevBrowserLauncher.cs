using System.Diagnostics;
using EggIncognito.Capture;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace EggIncognito.Startup;

public static class DevBrowserLauncher {
    private const string FallbackAddress = "http://localhost:5032";
    private static readonly TimeSpan DashboardSettle = TimeSpan.FromSeconds(1.5);

    public static void OpenDashboardOnStart(this WebApplication app, bool captureMode) {
        if (!ShouldOpen(app)) return;
        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(() => OpenAsync(app, captureMode)));
    }

    private static bool ShouldOpen(WebApplication app) =>
        app.Services.GetRequiredService<IServer>().GetType().Name == "KestrelServer"
        && app.Environment.IsDevelopment()
        && !app.Configuration.GetValue("NoBrowser", false);

    private static async Task OpenAsync(WebApplication app, bool captureMode) {
        string url = ServerAddress(app).TrimEnd('/') + (captureMode ? "/#api/capture" : "/#api");
        if (captureMode && await DashboardAlreadyOpenAsync(app)) {
            app.Logger.LogInformation("Dashboard already open (reconnected) - not opening a new tab.");
            return;
        }

        try {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        } catch (Exception ex) {
            app.Logger.LogWarning(ex, "Could not auto-open browser at {Url}", url);
        }
    }

    private static async Task<bool> DashboardAlreadyOpenAsync(WebApplication app) {
        await Task.Delay(DashboardSettle);
        return app.Services.GetRequiredService<CaptureSession>().Hub.HasSubscribers;
    }

    private static string ServerAddress(WebApplication app) =>
        app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?.Addresses.FirstOrDefault(a => a.StartsWith("http://", StringComparison.Ordinal))
        ?? FallbackAddress;
}
