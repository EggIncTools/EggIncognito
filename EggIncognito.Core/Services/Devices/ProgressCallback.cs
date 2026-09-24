namespace EggIncognito.Core.Services.Devices;

public static class ProgressCallback {
    public static Task ReportAsync(this Func<string, Task>? progress, string line) =>
        progress?.Invoke(line) ?? Task.CompletedTask;
}
