using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EggIncognito.Core;
using EggIncognito.Core.Services;
using EggIncognito.Services;

namespace EggIncognito.Components.Capture;

public sealed partial class CaptureViewState {
    public static readonly Regex EidRe = EidPattern.Contains;

    public HashSet<string> SensitiveKeys { get; } = [with(StringComparer.Ordinal), .. CaptureSensitiveKeys.All];

    public string RedactionMode { get; set; } = "blur";
    public bool ShowHeaders { get; set; }
    public bool AutoScroll { get; set; } = true;
    public bool CompareToKnown { get; set; }
    public string DefaultFormat { get; set; } = "json-tree";
    public bool SetupExpanded { get; set; } = true;

    public bool IsBlurMode => RedactionMode == "blur";
    public bool IsRedactMode => RedactionMode == "redact";

    public bool ShowRawHeaders => RedactionMode == "off";

    public string? PickJson(string? redacted, string? raw) =>
        RedactionMode == "redact" ? redacted : raw ?? redacted;

    public bool IsSensitiveKey(string? keyName) =>
        RedactionMode == "blur" && keyName is not null && SensitiveKeys.Contains(keyName);

    public bool LooksLikeEid(string s) => EidRe.IsMatch(s);

    public (string Text, bool Blur) RedactParamValue(string value) {
        if (RedactionMode == "redact" && EidRe.IsMatch(value)) return ("redacted-eid", false);
        return (value, RedactionMode == "blur" && EidRe.IsMatch(value));
    }

    public IEnumerable<(string Text, bool Blur)> RenderRedactedPath(string path) {
        string[] parts = MyRegex().Split(path);
        foreach (string part in parts) {
            if (part.Length == 0) continue;
            if (EidRe.IsMatch(part)) {
                if (RedactionMode == "redact") {
                    yield return ("redacted-eid", false);
                    continue;
                }

                yield return (part, RedactionMode == "blur");
            } else {
                yield return (part, false);
            }
        }
    }

    public HashSet<string> CollectSensitiveValues(JsonNode? value) {
        var outSet = new HashSet<string>(StringComparer.Ordinal);
        Visit(value, null, outSet);
        return outSet;
    }

    private void Visit(JsonNode? v, string? keyName, HashSet<string> outSet) {
        switch (v) {
            case null:
                return;
            case JsonArray arr:
                foreach (var item in arr) Visit(item, keyName, outSet);
                return;
            case JsonObject obj:
                foreach (var kv in obj) Visit(kv.Value, kv.Key, outSet);
                return;
            default:
                var jv = (JsonValue)v;
                string s = jv.TryGetValue(out string? str) && str is not null ? str : jv.ToJsonString();
                bool sensitive = (keyName is not null && SensitiveKeys.Contains(keyName)) || EidRe.IsMatch(s);
                if (sensitive) outSet.Add(s);
                return;
        }
    }

    [GeneratedRegex("(/)")]
    private static partial Regex MyRegex();
}

public static class CaptureHelpers {
    public static string StatusClass(int status) => status switch {
        >= 200 and < 300 => "status-2xx",
        >= 300 and < 400 => "status-3xx",
        >= 500 => "status-5xx",
        _ => "status-4xx"
    };

    public static string FormatBytes(long bytes) => ByteFormat.Humanize(bytes);

    public static OutcomeMeta? Outcome(string? outcome) => outcome switch {
        "wrote" => new OutcomeMeta("wrote", "good", "New endpoint written to disk (none existed before)."),
        "upd" => new OutcomeMeta("upd", "good", "Updated - an empty/placeholder endpoint was filled in."),
        "diff" => new OutcomeMeta("diff", "warn",
            "Differs from the saved endpoint - staged for review, not overwritten."),
        "loss" => new OutcomeMeta("loss", "bad",
            "Could not decode into an endpoint (no proto type / unparseable) - nothing saved."),
        "same" => new OutcomeMeta("same", "same", "Identical to the saved endpoint - no change."),
        _ => null
    };

    public static bool HasDiffCounts(string? outcome, int added, int removed) =>
        outcome == "diff" && (added > 0 || removed > 0);

    public sealed record OutcomeMeta(string Label, string Kind, string Desc);
}
