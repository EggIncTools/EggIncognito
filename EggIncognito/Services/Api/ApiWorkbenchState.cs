using System.Reflection;
using EggIdentity.UI;
using EggIncognito.Components.Capture;
using EggIncognito.Components.Inspector;
using EggIncognito.Core.Services;
using EggIncognito.Models.Data;
using EggIncognito.Models.Inspector;
using EggIncognito.Services.Inspector;
using EggIncognito.Services.Workbench;
using Google.Protobuf.Reflection;

namespace EggIncognito.Services.Api;

public sealed class ApiWorkbenchState : WorkbenchStateBase {
    public const string ModeDocs = "docs";
    public const string ModeApis = "apis";
    public const string ModeData = "data";
    public const string ModeCapture = "capture";

    public static readonly string[] PlatformOptions = [
        .. typeof(Ei.Platform).GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => f.GetCustomAttribute<OriginalNameAttribute>()?.Name ?? f.Name)
    ];

    private static readonly IReadOnlyList<WorkbenchMode> RawModes = [
        new(ModeDocs, "Docs"),
        new(ModeApis, "APIs"),
        new(ModeData, "Data"),
        new(ModeCapture, "Capture")
    ];

    public override IReadOnlyList<(string Key, string Label, int? Count)> Modes { get; } =
        [.. RawModes.Select(m => (m.Key, m.Label, m.Count))];

    public override string DefaultMode => ModeApis;

    public static string ModeFor(ApiSelectionKind kind) {
        return kind switch {
            ApiSelectionKind.Dataset => ModeData,
            ApiSelectionKind.Capture => ModeCapture,
            ApiSelectionKind.Docs => ModeDocs,
            _ => ModeApis
        };
    }

    public RouteInfo? Selected { get; set; }
    public DocSubjectRef? Docs { get; set; }
    public string DocsFilter { get; set; } = "";

    public List<EnvRow> EnvRows { get; set; } = [];
    public bool EnvOpen { get; set; } = true;
    public bool EnvValidated { get; set; }
    public bool EnvValidating { get; set; }
    public string? EnvError { get; set; }
    public List<FieldNode>? FieldNodes { get; set; }
    public string PathParam { get; set; } = "";
    public bool RawMode { get; set; }
    public string RawJson { get; set; } = "{}";
    public string? RawError { get; set; }

    public InspectorTarget Target { get; set; } = InspectorTarget.Mock;
    public bool Sealed { get; set; }
    public string CustomTarget { get; set; } = "";

    public bool Busy { get; set; }
    public BuildResponse? LastBuild { get; set; }
    public List<TransportStage>? BuildStages { get; set; }
    public SendResponse? Response { get; set; }
    public DiagnoseDto? Diagnosis { get; set; }

    public bool HistoryEnabled { get; set; } = true;
    public List<InspectorHistoryEntry> History { get; set; } = [];

    public RinfoSeed Rinfo { get; set; } = new();
    public string[] RecentEids { get; set; } = [];
    public bool HasSalt { get; set; }
    public string? Notice { get; set; }

    public bool LiveDisabled { get; set; }
    public bool SealedAvailable { get; set; }
    public bool CanSaveDb { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsContributor { get; set; }
    public bool Hosted { get; set; }

    public bool CanBuild => Selected is not null && !Busy;
    public bool CanSend => LastBuild is not null && !Busy;

    public InspectorRef Ref() => new(Selected?.Path, null);

    public void ClearTransaction() {
        LastBuild = null;
        BuildStages = null;
        Response = null;
        Diagnosis = null;
    }

    public void SeedEnvRows() {
        EnvRows = [
            new EnvRow {
                Key = "eiUserId", ValueType = EnvValueType.String, Editor = EnvEditor.Eid,
                Hint = "EI...", Value = Rinfo.EiUserId
            },
            new EnvRow {
                Key = "clientVersion", ValueType = EnvValueType.Number, Editor = EnvEditor.Int,
                Hint = "integer", Value = Rinfo.ClientVersion
            },
            new EnvRow {
                Key = "version", ValueType = EnvValueType.String, Editor = EnvEditor.Version,
                Hint = "major.minor.patch", Value = Rinfo.Version
            },
            new EnvRow {
                Key = "build", ValueType = EnvValueType.String, Editor = EnvEditor.Build,
                Value = Rinfo.Build
            },
            new EnvRow {
                Key = "platform", ValueType = EnvValueType.String, Editor = EnvEditor.Select,
                Options = PlatformOptions, Value = Rinfo.Platform
            },
            new EnvRow {
                Key = "country", ValueType = EnvValueType.String, Editor = EnvEditor.Code,
                Value = Rinfo.Country
            },
            new EnvRow {
                Key = "language", ValueType = EnvValueType.String, Editor = EnvEditor.Code,
                Value = Rinfo.Language
            },
            new EnvRow {
                Key = "debug", ValueType = EnvValueType.Boolean, Editor = EnvEditor.Bool,
                Value = Rinfo.Debug ? "true" : "false"
            }
        ];
    }

    public void SyncRinfoFromRow(EnvRow row) {
        switch (row.Key) {
            case "eiUserId":
                Rinfo.EiUserId = row.Value;
                break;
            case "clientVersion":
                Rinfo.ClientVersion = row.Value;
                break;
            case "version":
                Rinfo.Version = row.Value;
                break;
            case "build":
                Rinfo.Build = row.Value;
                break;
            case "platform":
                Rinfo.Platform = row.Value;
                break;
            case "country":
                Rinfo.Country = row.Value;
                break;
            case "language":
                Rinfo.Language = row.Value;
                break;
            case "debug":
                Rinfo.Debug = row.Value == "true";
                break;
        }
    }

    public void ApplyEnvLock() {
        if (FieldNodes is null) return;
        var env = EnvCollector.AsStrings(EnvRows);
        FieldTreeBuilder.ApplyEnvLock(FieldNodes, env);
        FieldTreeBuilder.ApplyEnvDefaults(FieldNodes, env);
    }

    public bool NoFillableFields =>
        FieldNodes is { Count: > 0 } && FieldNodes.All(n => n.Locked);

#pragma warning disable IDE0028
    private readonly Dictionary<string, ApiSelectionMemory> _memory = new(StringComparer.Ordinal);
#pragma warning restore IDE0028

    public ApiSelectionKind Kind {
        get;
        set {
            field = value;
            Mode = ModeFor(value);
        }
    } = ApiSelectionKind.Endpoint;

    public void RememberSelection() =>
        _memory[ModeFor(Kind)] = new ApiSelectionMemory(Kind, Group, Id, Sub, Docs);

    public bool RestoreSelection(string mode) {
        if (!_memory.TryGetValue(mode, out var memory)) return false;
        if (memory.Kind == ApiSelectionKind.Dataset) {
            if (memory.Group.Length == 0 || memory.Id.Length == 0) return false;
            SelectDataset(memory.Group, memory.Id, memory.Sub);
            return true;
        }

        if (memory.Kind == ApiSelectionKind.Docs) {
            if (memory.Docs is not { Key.Length: > 0 } docs) return false;
            Docs = docs;
            Kind = ApiSelectionKind.Docs;
            return true;
        }

        Kind = memory.Kind;
        return true;
    }

    public string Group { get; set; } = "";
    public string Id { get; set; } = "";
    public string? Sub { get; set; }
    public List<DataSourceRow>? Sources { get; set; }
#pragma warning disable IDE0028
    public Dictionary<string, string> Names { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Formats { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, DataPayloadEntry> Payloads { get; } = new(StringComparer.Ordinal);
#pragma warning restore IDE0028
    public CaptureViewState View { get; } = new();

    public string? PendingEndpointPath { get; set; }
    public string? PendingObjectName { get; set; }

    public bool HasDataset => Kind == ApiSelectionKind.Dataset && Group.Length > 0 && Id.Length > 0;

    public string DatasetKey => Sub is { Length: > 0 } sub ? $"{Group}/{Id}/{sub}" : $"{Group}/{Id}";

    public string NameFor(string key) => Names.GetValueOrDefault(key, "");

    public DataSourceRow? CurrentDataset() {
        var root = Sources?.FirstOrDefault(s => s.Group == Group && s.Id == Id);
        if (root is null) return null;
        return Sub is not { Length: > 0 } sub ? root : root.Children?.FirstOrDefault(c => c.Id == sub);
    }

    public void SelectDataset(string group, string id, string? sub) {
        Kind = ApiSelectionKind.Dataset;
        Group = group;
        Id = id;
        Sub = string.IsNullOrEmpty(sub) ? null : sub;
    }

    public override string HashPrefix => "api";

    public override string? Hash() {
        return Kind switch {
            ApiSelectionKind.Dataset when Group.Length > 0 && Id.Length > 0 =>
                Sub is { Length: > 0 } sub ? $"api/data/{Group}/{Id}/{sub}" : $"api/data/{Group}/{Id}",
            ApiSelectionKind.Routes => "api/routes",
            ApiSelectionKind.Capture => "api/capture",
            ApiSelectionKind.Docs =>
                Docs is { Key.Length: > 0 } docs ? $"api/docs/{docs.Slug}/{docs.Key}" : "api/docs",
            _ => MockHash()
        };
    }

    private string MockHash() {
        var formatted = InspectorRefParser.Format(Ref());
        return formatted.Length > 0 ? $"api/{formatted}" : "api";
    }

    public override bool ApplyHash(string? hash) {
        string body = (hash ?? "").TrimStart('#');
        if (body.StartsWith("data/", StringComparison.Ordinal)) body = "api/" + body;
        if (!body.StartsWith("api", StringComparison.Ordinal)) return false;
        string rest = body.Length > 3 && body[3] == '/' ? body[4..] : body == "api" ? "" : null!;
        if (rest is null) return false;

        if (rest.StartsWith("data/", StringComparison.Ordinal)) {
            string[] parts = rest["data/".Length..].Split('/');
            if (parts.Length is < 2 or > 3 || parts.Any(p => p.Length == 0 || p.Contains('.', StringComparison.Ordinal))) return false;
            SelectDataset(parts[0], parts[1], parts.Length == 3 ? parts[2] : null);
            return true;
        }

        if (rest == "docs") {
            Kind = ApiSelectionKind.Docs;
            return true;
        }

        if (rest.StartsWith("docs/", StringComparison.Ordinal)) {
            string tail = rest["docs/".Length..];
            int slash = tail.IndexOf('/');
            if (slash <= 0 || slash == tail.Length - 1) return false;
            if (!DocSubjectRef.TryParse(tail[..slash], tail[(slash + 1)..], out var docs)) return false;
            Docs = docs;
            Kind = ApiSelectionKind.Docs;
            return true;
        }

        switch (rest) {
            case "keys":
            case "keys/all":
                return false;
            case "routes":
                Kind = ApiSelectionKind.Routes;
                return true;
            case "capture":
                Kind = ApiSelectionKind.Capture;
                return true;
        }

        Kind = ApiSelectionKind.Endpoint;
        var mock = InspectorRefParser.Parse(rest);
        PendingEndpointPath = mock.EndpointPath;
        PendingObjectName = mock.ObjectName;
        return true;
    }
}
