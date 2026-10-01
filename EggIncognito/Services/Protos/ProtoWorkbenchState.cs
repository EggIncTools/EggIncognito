using EggIdentity.UI;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Services.Workbench;

namespace EggIncognito.Services.Protos;

public sealed record ExtractResult {
    public bool Ok { get; init; }
    public string? Proto { get; init; }
    public string? Diagnostics { get; init; }
    public string? ProtoSha { get; init; }
    public IReadOnlyList<string> Messages { get; init; } = [];
    public string? AppVersion { get; init; }
    public string? Build { get; init; }
    public int? ClientVersion { get; init; }
    public string? FileName { get; init; }
    public string? FileSha { get; init; }
}

public sealed class StagedEntry {
    public Guid Id { get; } = Guid.NewGuid();
    public required int Token { get; init; }
    public required string FileName { get; init; }
    public long Size { get; init; }
    public string Status { get; set; } = "queued";
    public string? Step { get; set; }
    public ExtractResult? Result { get; set; }
    public string? Error { get; set; }
    public string Platform { get; set; } = "ios";
    public string AppVersion { get; set; } = "";
    public string Build { get; set; } = "";
    public string ClientVersionText { get; set; } = "";
    public bool IsDone => Status == "done";
    public bool IsAnalyzed => IsDone && Result is { Ok: true };
}

public sealed record GroupStatus(
    bool Known, bool Pending, bool Offered, bool Failed = false, bool InRegistry = false, bool Archived = false,
    bool ShaDiffers = false, bool Rejected = false) {
    public bool Offerable => !Known && !Pending && !Offered && !Failed && !Rejected;
    public bool Flaggable => Known && (!Archived || ShaDiffers) && !Pending && !Offered && !Failed;
}

public sealed record DiffBundle(
    IReadOnlyList<DiffOp> LineOps,
    SideBySideResult Split,
    string Unified,
    ProtoDiffResult Structural,
    ProtoDiffSummary Summary);

public sealed class ProtoWorkbenchState : WorkbenchStateBase {
    public override IReadOnlyList<(string Key, string Label, int? Count)> Modes { get; } =
        [.. ProtoRefParser.Modes.Select(m => new WorkbenchMode(m, m)).Select(m => (m.Key, m.Label, m.Count))];

    public List<StagedEntry> Entries { get; } = [];
    public Guid? SelectedId { get; set; }
    public Dictionary<string, Guid> GroupWinners { get; } = [];
    public Dictionary<string, GroupStatus> GroupStatuses { get; } = [];

    public IReadOnlyList<ProtoRegistryRow> Registry { get; set; } = [];
    public DateTime RegistryLoadedAt { get; set; }
    public Dictionary<string, string> TextCache { get; } = [];
    public Dictionary<string, string> TextForms { get; } = [];
    public Dictionary<string, string> CanonicalShas { get; } = [];
    public RegistryQuery Query { get; set; } = RegistryQuery.Empty;
    public Dictionary<string, string> ViewFilters { get; } = [];
    public ProtoRef? A { get; set; }
    public ProtoRef? B { get; set; }
    public DiffBundle? Cached { get; set; }
    public string? CachedKey { get; set; }

    public StagedEntry? Find(Guid? id) => id is { } g ? Entries.Find(e => e.Id == g) : null;
}
