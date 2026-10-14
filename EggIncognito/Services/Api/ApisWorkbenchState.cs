using System.Reflection;
using EggIdentity.UI;
using EggIncognito.Components.Inspector;
using EggIncognito.Core.Services;
using EggIncognito.Models.Inspector;
using EggIncognito.Services.Inspector;
using Google.Protobuf.Reflection;

namespace EggIncognito.Services.Api;

public enum ApisPaneKind {
    Endpoint,
    Routes
}

public sealed class ApisWorkbenchState : WorkbenchStateBase {
    public static readonly string[] PlatformOptions = [
        .. typeof(Ei.Platform).GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => f.GetCustomAttribute<OriginalNameAttribute>()?.Name ?? f.Name)
    ];

    public override IReadOnlyList<(string Key, string Label, int? Count)> Modes { get; } = [];

    public override string HashPrefix => "apis";

    public ApisPaneKind Kind { get; set; } = ApisPaneKind.Endpoint;

    public RouteInfo? Selected { get; set; }

    public bool DocsOpen { get; set; }
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

    public bool HistoryOpen { get; set; }
    public List<InspectorHistoryEntry> History { get; set; } = [];

    public event Action<DocSubjectRef>? DocsRequested;

    public void RequestDocs(DocSubjectRef subject) => DocsRequested?.Invoke(subject);

    public void RequestMessageDocs(string name) => RequestDocs(new DocSubjectRef(DocSubjectKind.Message, name));

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

    public string? PendingEndpointPath { get; set; }
    public string? PendingObjectName { get; set; }

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

    public override string? Hash() {
        if (DocsOpen) return Docs is { Key.Length: > 0 } docs ? $"apis/docs/{docs.Slug}/{docs.Key}" : "apis/docs";
        if (Kind == ApisPaneKind.Routes) return "apis/routes";
        return MockHash();
    }

    private string MockHash() {
        var formatted = InspectorRefParser.Format(Ref());
        return formatted.Length > 0 ? $"apis/{formatted}" : "apis";
    }

    public override bool ApplyHash(string? hash) {
        string body = (hash ?? "").TrimStart('#');
        if (!body.StartsWith("apis", StringComparison.Ordinal)) return false;
        string? rest = body.Length > 4 && body[4] == '/' ? body[5..] : body == "apis" ? "" : null;
        if (rest is null) return false;

        if (rest == "docs") {
            DocsOpen = true;
            Docs = null;
            return true;
        }

        if (rest.StartsWith("docs/", StringComparison.Ordinal)) {
            string tail = rest["docs/".Length..];
            int slash = tail.IndexOf('/');
            if (slash <= 0 || slash == tail.Length - 1) return false;
            if (!DocSubjectRef.TryParse(tail[..slash], tail[(slash + 1)..], out var docs)) return false;
            DocsOpen = true;
            Docs = docs;
            return true;
        }

        if (rest == "routes") {
            DocsOpen = false;
            Kind = ApisPaneKind.Routes;
            return true;
        }

        DocsOpen = false;
        Kind = ApisPaneKind.Endpoint;
        var mock = InspectorRefParser.Parse(rest);
        PendingEndpointPath = mock.EndpointPath;
        PendingObjectName = mock.ObjectName;
        return true;
    }
}
