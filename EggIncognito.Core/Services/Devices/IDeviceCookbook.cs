namespace EggIncognito.Core.Services.Devices;

public static class DeviceCookbookIds {
    public const string InstallApp = "install-app";
    public const string InstallCa = "install-ca";
    public const string LaunchApp = "launch-app";
    public const string DismissFirstRun = "dismiss-first-run";
    public const string BringUp = "bring-up";
    public const string Recert = "recert";
    public const string Readiness = "readiness";
    public const string AppAudit = "app-audit";
    public const string CreateIsland = "create-island";
    public const string InstallAppIsland = "install-app-island";
    public const string LaunchIsland = "launch-island";
    public const string RemoveIsland = "remove-island";
    public const string IslandIntegrity = "island-integrity";
}

public sealed record DeviceCookbookOption(
    string Value, string Label, bool Recommended = false, string? Detail = null);

public sealed record DeviceCookbookInfo(
    string Id,
    string Title,
    string Summary,
    bool Available,
    string? Unavailable = null,
    string? ArgumentLabel = null,
    IReadOnlyList<DeviceCookbookOption>? Options = null) {
    public string Group { get; init; } = CookbookGroups.Step;
}

public sealed record DeviceCookbookRequest(string CookbookId, string? Argument = null, int? AndroidUserId = null);

public sealed record DeviceCookbookRun(
    bool Ok,
    string CookbookId,
    IReadOnlyList<string> Log,
    string? FailedStep = null,
    string? Note = null,
    string? FailedStepTitle = null) {
    public IReadOnlyList<CookbookStepResult> Steps { get; init; } = [];

    public string? Failure {
        get {
            if (Ok) return null;
            string? where = FailedStepTitle ?? FailedStep;
            if (string.IsNullOrEmpty(where)) return Note;
            return string.IsNullOrEmpty(Note) ? $"{where} failed" : $"{where}: {Note}";
        }
    }
}

public sealed record DeviceCookbookContext(
    DeviceTarget Target,
    string? Argument,
    Func<string, Task> Progress,
    int? AndroidUserId = null);

public interface IDeviceCookbook {
    string Id { get; }
    string Title { get; }
    string Summary { get; }
    Task<DeviceCookbookInfo> DescribeAsync(DeviceTarget target, CancellationToken ct);
    Task<DeviceCookbookRun> RunAsync(DeviceCookbookContext context, CancellationToken ct);
}

public interface IDeviceCookbooks {
    Task<IReadOnlyList<DeviceCookbookInfo>> DescribeAllAsync(DeviceTarget target, CancellationToken ct);
    IDeviceCookbook? Find(string cookbookId);
}

public sealed class DeviceCookbooks(IEnumerable<IDeviceCookbook> cookbooks) : IDeviceCookbooks {
    public static readonly TimeSpan DescribeBudget = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, IDeviceCookbook> _byId =
        cookbooks.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<DeviceCookbookInfo>> DescribeAllAsync(
        DeviceTarget target, CancellationToken ct) {
        var ordered = _byId.Values.OrderBy(c => c.Id, StringComparer.Ordinal);
        return await Task.WhenAll(ordered.Select(c => DescribeBoundedAsync(c, target, DescribeBudget, ct)));
    }

    public static async Task<DeviceCookbookInfo> DescribeBoundedAsync(IDeviceCookbook cookbook, DeviceTarget target,
        TimeSpan budget, CancellationToken ct) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget);
        try {
            return await cookbook.DescribeAsync(target, cts.Token).WaitAsync(cts.Token);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            return Unavailable(cookbook, $"checking availability took longer than {budget.TotalSeconds:0}s");
        } catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException) {
            return Unavailable(cookbook, $"checking availability failed: {ex.Message}");
        }
    }

    private static DeviceCookbookInfo Unavailable(IDeviceCookbook cookbook, string reason) =>
        new(cookbook.Id, cookbook.Title, cookbook.Summary, false, reason);

    public IDeviceCookbook? Find(string cookbookId) =>
        !string.IsNullOrEmpty(cookbookId) && _byId.TryGetValue(cookbookId, out var c) ? c : null;
}
