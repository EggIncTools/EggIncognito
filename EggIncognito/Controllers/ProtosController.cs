using System.Text;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Services;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/protos")]
[ApiAccess(ApiAccessLevel.Public)]
[EnableRateLimiting("fetch")]
public sealed class ProtosController : ApiControllerBase {
    private const string FormatText = "text";
    private const string FormatUnified = "unified";
    private const string FormatSplit = "split";
    private const string FormatJson = "json";
    private static readonly string[] DiffFormats = [FormatText, FormatUnified, FormatJson, FormatSplit];
    private static readonly string[] TruthyValues = ["1", "true", "yes", "on"];

    [HttpGet("versions")]
    public async Task<IActionResult> Versions([FromServices] ProtoRegistryStore? store, [FromQuery] string? platform,
        CancellationToken ct) {
        if (store is not { } s) return Ok(Array.Empty<object>());
        var rows = await s.ListAsync(platform, ct);
        var orders = await s.ShaOrdersAsync(ct);

        return Ok(rows.Select(r => new {
            r.Id,
            r.CanonicalId,
            r.Platform,
            r.AppVersion,
            r.Build,
            r.ClientVersion,
            r.Source,
            r.Package,
            r.ProtoSha,
            r.DetectedAt,
            buildFlag = ProtoVersionQuality.BuildQualityFlag(r.Platform, r.Build),
            sortOrder = orders.TryGetValue(r.ProtoSha ?? "", out int so) ? so : 0
        }));
    }

    [HttpGet("versions/{platform}/{build}")]
    public async Task<IActionResult> Get(string platform, string build, [FromServices] ProtoRegistryStore? store,
        CancellationToken ct) {
        if (store is not { } s) return NotFound();
        var row = await s.GetAsync(platform, build, ct);
        if (row is null) return NotFound();
        var pp = await s.GetProtoAsync(row.Id, ct);
        return Ok(new {
            row.Platform,
            row.AppVersion,
            row.Build,
            row.ClientVersion,
            row.Source,
            row.Package,
            row.ProtoSha,
            row.DetectedAt,
            messages = pp is null ? "[]" : pp.MessageIndex,
            hasProto = pp is not null
        });
    }

    [HttpGet("versions/{platform}/{build}/proto")]
    public async Task<IActionResult> Proto(
        string platform, string build, [FromQuery] string? form,
        [FromServices] ProtoRegistryStore? store, CancellationToken ct) {
        if (store is not { } s) return NotFound();
        var (raw, canonical) = await s.GetCanonicalForVersionAsync(platform, build, ct);
        if (raw is null) return NotFound();

        bool wantRaw = string.Equals(form, ProtoDisplayForm.Raw, StringComparison.OrdinalIgnoreCase);
        bool useCanonical = !wantRaw && canonical.Ok;

        Response.Headers["X-Proto-Form"] = useCanonical ? ProtoDisplayForm.Canonical : ProtoDisplayForm.Raw;
        if (!string.IsNullOrEmpty(canonical.Sha)) Response.Headers["X-Proto-Canonical-Sha"] = canonical.Sha;

        return Content(useCanonical ? canonical.Text! : raw.ProtoText, "text/plain");
    }

    [HttpGet("sources")]
    public async Task<IActionResult> Sources([FromServices] ProtoRegistryStore? store, CancellationToken ct) =>
        store is not { } s ? Ok(new Dictionary<string, int>()) : Ok(await s.SourceCountsAsync(ct));

    [HttpGet("latest")]
    public async Task<IActionResult> Latest([FromQuery] string platform = "android",
        [FromServices] ProtoRegistryStore? store = null, CancellationToken ct = default) {
        if (store is not { } s) return NotFound();
        var rows = await s.ListAsync(platform, ct);

        var r = rows
            .OrderByDescending(p => ProtoVersionQuality.LatestSortKey(p.Platform, p.Build, p.AppVersion))
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefault();
        return r is null
            ? NotFound()
            : Ok(new { r.Platform, r.AppVersion, r.Build, r.ClientVersion, r.Source, r.ProtoSha, r.DetectedAt });
    }

    [HttpGet("diff")]
    public async Task<IActionResult> Diff(
        [FromQuery] string from, [FromQuery] string to, [FromQuery] string platform = "android",
        [FromQuery] string? format = null, [FromQuery] int context = 3, [FromQuery] string? download = null,
        [FromServices] ProtoRegistryStore? store = null, CancellationToken ct = default) {
        string fmt = string.IsNullOrWhiteSpace(format) ? FormatText : format.Trim();
        if (!DiffFormats.Contains(fmt, StringComparer.OrdinalIgnoreCase))
            return Fail(400, "format must be one of text, unified, json, split");

        if (store is not { } s) return NotFound();
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return Fail(400, "from and to required");

        var (fromRaw, fromCanonical) = await LoadProtoText(s, platform, from, ct);
        var (toRaw, toCanonical) = await LoadProtoText(s, platform, to, ct);
        if (fromRaw is null || toRaw is null) return NotFound();

        var (fromText, toText, usedForm) = ProtoDisplayForm.Pair(fromCanonical, fromRaw, toCanonical, toRaw);
        Response.Headers["X-Proto-Form"] = usedForm;

        bool attach = Truthy(download);

        if (IsFormat(fmt, FormatText)) {
            if (attach) Attach(from, to, "txt");
            return Content(ProtoDiff.Diff(fromText, toText), "text/plain");
        }

        if (IsFormat(fmt, FormatUnified)) {
            string patch = UnifiedDiffWriter.Write(fromText, toText, new UnifiedDiffOptions(
                Math.Clamp(context, 0, 50),
                LabelA: $"{platform} {from}",
                LabelB: $"{platform} {to}"));
            if (attach) Attach(from, to, "diff");
            return Content(patch, "text/plain");
        }

        if (IsFormat(fmt, FormatSplit)) {
            var split = SideBySideDiffBuilder.Build(fromText, toText);
            if (attach) Attach(from, to, "json");
            return Ok(new { rows = split.Rows, hunkStarts = split.HunkStarts });
        }

        var structural = ProtoDiff.Compute(fromText, toText);
        var lineOps = MyersDiff.Compute(
            UnifiedDiffWriter.SplitLines(fromText), UnifiedDiffWriter.SplitLines(toText));
        if (attach) Attach(from, to, "json");
        return Ok(new { entries = structural.Entries, summary = ProtoDiffSummary.From(structural, lineOps) });
    }

    private void Attach(string from, string to, string extension) =>
        Response.Headers.ContentDisposition =
            $"attachment; filename=\"ei-{SafeName(from)}..{SafeName(to)}.{extension}\"";

    private static bool IsFormat(string value, string name) =>
        string.Equals(value, name, StringComparison.OrdinalIgnoreCase);

    private static bool Truthy(string? value) =>
        !string.IsNullOrWhiteSpace(value) && TruthyValues.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    private static string SafeName(string value) {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value) sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-');
        return sb.Length == 0 ? "proto" : sb.ToString();
    }

    private static async Task<(string? Raw, string? Canonical)> LoadProtoText(
        ProtoRegistryStore store, string platform, string build, CancellationToken ct) {
        var (raw, canonical) = await store.GetCanonicalForVersionAsync(platform, build, ct);
        return (raw?.ProtoText, canonical.Ok ? canonical.Text : null);
    }
}
