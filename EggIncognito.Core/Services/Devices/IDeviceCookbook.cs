namespace EggIncognito.Core.Services.Devices;

public static class DeviceCookbookIds {
    public const string InstallApp = "install-app";
    public const string InstallCa = "install-ca";
    public const string LaunchApp = "launch-app";
    public const string DismissFirstRun = "dismiss-first-run";
    public const string BringUp = "bring-up";
    public const string Recert = "recert";
    public const string Readiness = "readiness";
    public const string InstallIntegrity = "install-integrity";
    public const string ActivateIntegrity = "activate-integrity";
    public const string SeedAudit = "seed-audit";
    public const string IntegrityAudit = "integrity-audit";
    public const string AppAudit = "app-audit";
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

public sealed record DeviceCookbookRequest(string CookbookId, string? Argument = null);

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
    Action<string> Progress);

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
    private readonly Dictionary<string, IDeviceCookbook> _byId =
        cookbooks.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<DeviceCookbookInfo>> DescribeAllAsync(
        DeviceTarget target, CancellationToken ct) {
        var described = new List<DeviceCookbookInfo>();
        foreach (var cookbook in _byId.Values.OrderBy(c => c.Id, StringComparer.Ordinal))
            described.Add(await cookbook.DescribeAsync(target, ct));
        return described;
    }

    public IDeviceCookbook? Find(string cookbookId) =>
        !string.IsNullOrEmpty(cookbookId) && _byId.TryGetValue(cookbookId, out var c) ? c : null;
}
