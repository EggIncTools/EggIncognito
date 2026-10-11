using System.Globalization;
using System.Text.Json.Nodes;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Core.Services.ProtoExtract.Decomp;
using EggIncognito.Data.Services;
using EggIncognito.DeviceTools;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/decomp")]
[ApiAccess(ApiAccessLevel.Contributor)]
public sealed class DecompController(GameBinaryProvider binaries) : ApiControllerBase {

    [HttpGet("symbols")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> Symbols([FromQuery] string? filter, [FromQuery] string? device,
        CancellationToken ct) {
        (bool ok, byte[]? bin, string? diag) = await binaries.GetBinaryAsync(device, ct);
        if (!ok || bin is null) return Ok(new { ok = false, diagnostics = diag });

        var syms = MachoSymbols.Read(bin);
        var named = syms.Where(s => !string.IsNullOrEmpty(s.Name)).ToList();
        var matches = string.IsNullOrEmpty(filter)
            ? named.Take(40).Select(s => s.Name)
            : named.Where(s => s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).Take(60)
                .Select(s => s.Name);
        return Ok(new {
            ok = true,
            totalSymbols = syms.Count,
            namedSymbols = named.Count,
            withAddress = named.Count(s => s.Value != 0),
            sample = matches.ToList()
        });
    }

    [HttpGet("function-constants")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> FunctionConstants([FromQuery] string name, [FromQuery] string? device,
        CancellationToken ct) {
        return string.IsNullOrWhiteSpace(name)
            ? Fail(400, "name required")
            : await ExtractAsync([name], device, ct);
    }

    [HttpGet("galaxy-particle")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> GalaxyParticle([FromQuery] string? device, CancellationToken ct) {
        try {
            (bool ok, byte[]? bin, string? diag) = await binaries.GetBinaryAsync(device, ct);
            if (!ok || bin is null) return Ok(new { ok = false, diagnostics = diag });

            var motion = FunctionConstantExtractor.Extract(bin, ["DrawableGalaxyParticle6updateEf"]);
            var spawn = FunctionConstantExtractor.Extract(bin, ["GalaxyParticle7onBirthEP14ParticleSystem"]);
            return Ok(new { ok = true, motion = Shape(motion), spawn = Shape(spawn) });
        } catch (DllNotFoundException) {
            return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" });
        } catch (Exception ex) {
            return Ok(new { ok = false, diagnostics = ex.Message });
        }
    }

    [HttpGet("recover")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> Recover(
        [FromQuery] string? name, [FromQuery] string? refVersion, [FromQuery] string? targetPath,
        CancellationToken ct) {
        try {
            (bool ok, byte[]? refBytes, byte[]? tgtBytes, string? diag) =
                await binaries.GetRecoveryInputsAsync(refVersion, targetPath, ct);
            if (!ok || refBytes is null || tgtBytes is null) return Ok(new { ok = false, diagnostics = diag });

            string[] needles = string.IsNullOrWhiteSpace(name)
                ? ["GalaxyParticle6update", "GalaxyParticle7onBirth", "FarmScene10updateSilo"]
                : [name];
            var report = SymbolRecovery.Recover(refBytes, tgtBytes, needles);

            var extracted = needles.Select(n => {
                var ex = FunctionConstantExtractor.ExtractWith(tgtBytes, report.Symbols, [n]);
                return new { needle = n, result = Shape(ex) };
            }).ToList();

            return Ok(new {
                ok = true,
                tier = report.Tier,
                recovered = report.Recovered,
                requestedFound = report.RequestedFound,
                requestedMissing = report.RequestedMissing,
                diagnostics = report.Diagnostics,
                extracted
            });
        } catch (DllNotFoundException) {
            return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" });
        } catch (Exception ex) {
            return Ok(new { ok = false, diagnostics = ex.Message });
        }
    }

    [HttpGet("resolve-va")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> ResolveVa(
        [FromQuery] string name, [FromQuery] string? refVersion, [FromQuery] string? targetPath, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(name)) return Fail(400, "name required");
        try {
            (bool ok, byte[]? refBytes, byte[]? tgtBytes, string? diag) =
                await binaries.GetRecoveryInputsAsync(refVersion, targetPath, ct);
            if (!ok || refBytes is null || tgtBytes is null) return Ok(new { ok = false, diagnostics = diag });

            var report = SymbolRecovery.Recover(refBytes, tgtBytes, [name]);
            ulong textVm = 0, textOff = 0;
            if (MachoText.TryFindText(tgtBytes, out int tfo, out _, out ulong tvm)) {
                textVm = tvm;
                textOff = (ulong)tfo;
            }

            var exact = report.Symbols.Where(s => s.Name == name).ToList();
            if (exact.Count > 0)
                return VaResult(tgtBytes, report, exact[0].Name, exact[0].Value, textVm, textOff, "exact-recovered",
                    null);

            var embedded = report.Symbols
                .Where(s => s.Name != name && s.Name.Contains(name, StringComparison.Ordinal))
                .OrderBy(s => s.Name.Length).ToList();
            foreach (var lam in embedded) {
                var referrers = Arm64AddrRefResolver.FindReferrers(tgtBytes, lam.Value);
                if (referrers.Count > 0)
                    return VaResult(tgtBytes, report, name + " (via referrer of " + lam.Name + ")",
                        referrers[0].FunctionVa,
                        textVm, textOff, "addr-referrer", referrers.Take(5)
                            .Select(r => new { fnVa = "0x" + r.FunctionVa.ToString("x", CultureInfo.InvariantCulture), r.HitCount }).ToList());
            }

            return embedded.Count > 0
                ? VaResult(tgtBytes, report, embedded[0].Name, embedded[0].Value, textVm, textOff, "contains-fallback",
                    null)
                : Ok(new {
                    ok = false,
                    tier = report.Tier,
                    recovered = report.Recovered,
                    diagnostics = $"{name} not recovered and no referrer found"
                });
        } catch (DllNotFoundException) {
            return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" });
        } catch (Exception ex) {
            return Ok(new { ok = false, diagnostics = ex.Message });
        }
    }

    [HttpGet("effect")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult>
        Effect([FromQuery] string? name, [FromQuery] string? device, CancellationToken ct) {
        if (!string.Equals(name, "galaxy-particle", StringComparison.OrdinalIgnoreCase))
            return Fail(400, "unknown effect; supported: galaxy-particle");
        try {
            (bool ok, byte[]? bin, string? diag) = await binaries.GetBinaryAsync(device, ct);
            if (!ok || bin is null) return Ok(new { ok = false, diagnostics = diag });
            var model = EffectRecovery.Recover(
                bin, "DrawableGalaxyParticle6updateEf",
                new ConstExpr(27));
            return Content(model.ToJson().ToJsonString(), "application/json");
        } catch (DllNotFoundException) {
            return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" });
        } catch (Exception ex) {
            return Ok(new { ok = false, diagnostics = ex.Message });
        }
    }

    [HttpGet("farm-placement")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> FarmPlacement([FromQuery] string? device, CancellationToken ct) {
        try {
            (bool ok, byte[]? bin, string? diag) = await binaries.GetBinaryAsync(device, ct);
            if (!ok || bin is null) return Ok(new { ok = false, diagnostics = diag });
            var R = FarmPlacementRecovery.Recover;
            var json = new JsonObject {
                ["ok"] = true,
                ["missionControl"] = R(bin, "FarmScene17missionControlPos").ToJson(),
                ["fuelTank"] = R(bin, "FarmScene11fuelTankPos").ToJson(),
                ["hoa"] = R(bin, "FarmScene6hoaPos").ToJson()
            };
            return Content(json.ToJsonString(), "application/json");
        } catch (DllNotFoundException) {
            return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" });
        } catch (Exception ex) {
            return Ok(new { ok = false, diagnostics = ex.Message });
        }
    }

    [HttpGet("building-effects")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> BuildingEffects([FromQuery] string stem, [FromQuery] string? device,
        CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(stem)) return Fail(400, "stem required");
        try {
            (bool ok, byte[]? bin, _) = await binaries.GetBinaryAsync(device, ct);
            if (!ok || bin is null) return Ok(new { stem, effects = Array.Empty<object>() });
            var models = BuildingEffectResolver.Resolve(bin, stem);
            var arr = new JsonArray(models.Select(m => (JsonNode)m.ToJson()).ToArray());
            return Content(new JsonObject { ["stem"] = stem, ["effects"] = arr }.ToJsonString(), "application/json");
        } catch (DllNotFoundException) {
            return Ok(new { stem, effects = Array.Empty<object>() });
        } catch (Exception ex) {
            return Ok(new { stem, effects = Array.Empty<object>(), diagnostics = ex.Message });
        }
    }

    [HttpGet("hatchery-assembly")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> HatcheryAssembly([FromQuery] string? device, CancellationToken ct) {
        try {
            (bool ok, byte[]? bin, string? diag) = await binaries.GetBinaryAsync(device, ct);
            if (!ok || bin is null) return Ok(new { ok = false, diagnostics = diag });

            var asm = HatcheryAssemblyRecovery.Recover(bin);
            var main = FunctionConstantExtractor.Extract(bin, ["FarmScene14updateHatcheryEP14GameControllerb"]);

            var json = asm.ToJson();
            json["main"] = new JsonObject {
                ["function"] = main.FunctionName,
                ["floats"] = new JsonArray(main.Floats.Select(f => JsonValue.Create(f)).ToArray()),
                ["calls"] = new JsonArray(main.Calls.Select(c => JsonValue.Create(c)).ToArray())
            };

            FunctionConstantExtractor.ExtractResult Ex(string n) {
                return FunctionConstantExtractor.Extract(bin, [n]);
            }

            JsonObject Shaped(string label, string needle) {
                var r = Ex(needle);
                return new JsonObject {
                    ["label"] = label,
                    ["ok"] = r.Ok,
                    ["function"] = r.FunctionName,
                    ["floats"] = new JsonArray(r.Floats.Select(f => JsonValue.Create(f)).ToArray()),
                    ["calls"] = new JsonArray(r.Calls.Select(c => JsonValue.Create(c)).ToArray())
                };
            }

            json["helpers"] = new JsonArray(
                Shaped("rotate_pyramid", "FarmScene14rotate_pyramidEP14GameControlleri"),
                Shaped("fire_predicate", "updateHatcheryEP14GameControllerbE3$_6FbvEclEv"),
                Shaped("beam_lambda_$_5_body",
                    "updateHatcheryEP14GameControllerbE3$_5FN5Eigen6MatrixIfLi4ELi4ELi0ELi4ELi4EEEvEEclEv"));
            return Content(json.ToJsonString(), "application/json");
        } catch (DllNotFoundException) {
            return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" });
        } catch (Exception ex) {
            return Ok(new { ok = false, diagnostics = ex.Message });
        }
    }

    [HttpPost("particle-capture")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> ParticleCapture([FromQuery] string? addrOffset, [FromQuery] string platform = "ios",
        [FromQuery] string? device = null, [FromServices] IDevicePlatforms? platforms = null,
        [FromServices] IDeviceResolver? resolver = null, CancellationToken ct = default) {
        if (platforms is null)
            return Fail(503, "device platform registry unavailable");

        var target = await ResolveDeviceTargetAsync(resolver, device, platform, ct);
        if (target is null)
            return Fail(503, $"no enabled {platform} device");

        try {
            var result = await platforms.For(target.Platform)
                .CaptureParticlesAsync(target, DeviceScripts.ParticleCapture, addrOffset, ct);
            return result is { Ok: true, Value: { } model }
                ? Content(model.ToJson().ToJsonString(), "application/json")
                : Ok(new { ok = false, diagnostics = $"{result.Outcome}: {result.Note}" });
        } catch (Exception ex) {
            return Ok(new { ok = false, diagnostics = ex.Message });
        }
    }

    private static async Task<DeviceTarget?> ResolveDeviceTargetAsync(
        IDeviceResolver? resolver, string? deviceId, string platform, CancellationToken ct) {
        if (resolver is null) return null;
        try {
            var query = deviceId is null ? new DeviceQuery(Platform: platform) : new DeviceQuery(deviceId);
            var d = await resolver.ResolveAsync(query, ct);
            return d is null ? null : new DeviceTarget(d.Id, d.Platform, d.Target, d.Package);
        } catch {
            return null;
        }
    }

    [HttpGet("signature")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> Signature(
        [FromQuery] string name, [FromQuery] string? refVersion, [FromQuery] int instructions, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(name)) return Fail(400, "name required");
        if (instructions <= 0) instructions = 8;
        try {
            (_, byte[]? refBytes, _, string? diag) =
                await binaries.GetRecoveryInputsAsync(refVersion, "/dev/null", ct);

            if (refBytes is null) {
                (_, byte[]? rb, string? rdiag) = await binaries.GetBinaryAsync(null, ct);
                refBytes = rb;
                if (refBytes is null) return Ok(new { ok = false, diagnostics = rdiag ?? diag });
            }

            if (!MachoText.TryFindText(refBytes, out int tfo, out _, out ulong tvm))
                return Ok(new { ok = false, diagnostics = "reference has no __text" });
            var syms = MachoSymbols.Read(refBytes);
            if (!MachoSymbols.TryFindFunc(syms, [name], out var fn))
                return Ok(new { ok = false, diagnostics = $"symbol not found in reference: {name}" });

            var pat = Arm64Signature.Build(refBytes, fn.Start, fn.End, tvm, tfo, instructions);
            return Ok(new {
                ok = pat.Ok,
                name = fn.Name,
                refFunctionVa = "0x" + fn.Start.ToString("x", CultureInfo.InvariantCulture),
                refFunctionLen = (int)(fn.End - fn.Start),
                instructions = pat.Instructions,
                maskedWords = pat.MaskedWords,
                pattern = pat.FridaPattern,
                diagnostics = pat.Diagnostics,
                hint =
                    "frida: Memory.scan(module.base, module.size, pattern, {onMatch, onComplete}); fewer matches = better. Widen instructions if 0 matches, narrow if many."
            });
        } catch (DllNotFoundException) {
            return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" });
        } catch (Exception ex) {
            return Ok(new { ok = false, diagnostics = ex.Message });
        }
    }

    [HttpGet("disasm")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> Disasm(
        [FromQuery] string name, [FromQuery] string? device, [FromQuery] string mode = "list",
        [FromQuery] int max = 512, [FromQuery] bool live = false, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(name)) return Fail(400, "name required");
        try {
            (bool ok, byte[]? bin, var syms, string source, string? diag) = await ResolveBinaryAsync(device, live, ct);
            if (!ok || bin is null) return Ok(new { ok = false, diagnostics = diag });

            switch (mode.ToLowerInvariant()) {
                case "constants":
                    var ex = syms is null
                        ? FunctionConstantExtractor.Extract(bin, [name])
                        : FunctionConstantExtractor.ExtractWith(bin, syms, [name]);
                    return Ok(new { ok = ex.Ok, mode, source, result = Shape(ex) });

                case "addresses" or "table":
                    var scan = syms is null
                        ? Arm64DataTableReader.Scan(bin, [name])
                        : Arm64DataTableReader.ScanWith(bin, syms, [name]);
                    return Ok(new {
                        ok = scan.Ok,
                        mode,
                        source,
                        function = scan.FunctionName,
                        diagnostics = scan.Diagnostics,
                        addresses = scan.Addresses.Select(a => new {
                            va = "0x" + a.Va.ToString("x", CultureInfo.InvariantCulture),
                            a.Segment,
                            a.Section,
                            a.Via
                        }).ToList()
                    });

                case "list":
                default:
                    var lst = syms is null
                        ? Arm64DataTableReader.List(bin, [name], Math.Clamp(max, 1, 4096))
                        : Arm64DataTableReader.ListWith(bin, syms, [name], Math.Clamp(max, 1, 4096));
                    return Ok(new {
                        ok = lst.Ok,
                        mode = "list",
                        source,
                        function = lst.FunctionName,
                        start = "0x" + lst.Start.ToString("x", CultureInfo.InvariantCulture),
                        end = "0x" + lst.End.ToString("x", CultureInfo.InvariantCulture),
                        diagnostics = lst.Diagnostics,
                        instructions = lst.Instructions.Select(i => new {
                            va = "0x" + i.Va.ToString("x", CultureInfo.InvariantCulture),
                            i.Mnemonic,
                            i.Operands
                        }).ToList()
                    });
            }
        } catch (DllNotFoundException) {
            return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" });
        } catch (Exception ex) {
            return Ok(new { ok = false, diagnostics = ex.Message });
        }
    }

    [HttpGet("stored-binaries")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    public async Task<IActionResult> StoredBinaries([FromServices] GameBinaryStore store, CancellationToken ct) {
        var rows = await store.ListAsync(ct);
        return Ok(new {
            ok = true,
            count = rows.Count,
            binaries = rows.Select(b => new {
                b.Platform,
                version = b.AppVersion,
                sha256 = b.Sha256,
                byteSize = b.ByteSize,
                nativeSymbols = b.NativeSymbolCount,
                effectiveSymbols = b.EffectiveSymbolCount,
                b.Source,
                pulledAt = b.PulledAt
            }).ToList()
        });
    }

    [HttpGet("stored-binaries/{platform}/{version}/download")]
    [EnableRateLimiting("fetch")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    [RequiresDb]
    public async Task<IActionResult> DownloadStoredBinary(string platform, string version,
        [FromServices] GameBinaryStore store, CancellationToken ct) {
        var row = await store.GetAsync(platform, version, ct);
        if (row is null || row.ByteSize == 0)
            return Fail(404, $"no stored binary {platform} {version}");

        Response.Headers.CacheControl = "private, max-age=3600";
        Response.Headers.ETag = $"\"{row.Sha256}\"";
        return File(await store.BytesAsync(row, ct), "application/octet-stream",
            StoredBinaryFileName(row.Platform, row.AppVersion));
    }

    private static string StoredBinaryFileName(string platform, string version) {
        string safeVersion = string.Concat(version.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'));
        return Platforms.Matches(platform, Platforms.Android)
            ? $"libegginc-{safeVersion}.so"
            : $"egginc-{platform}-{safeVersion}";
    }

    [HttpDelete("stored-binaries/{platform}/{version}")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    public async Task<IActionResult> DeleteStoredBinary(string platform, string version,
        [FromServices] GameBinaryStore store, CancellationToken ct) {
        bool removed = await store.DeleteAsync(platform, version, ct);
        return removed
            ? Ok(new { ok = true, platform, version })
            : Fail(404, $"no stored binary {platform} {version}");
    }

    [HttpGet("harvested")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> Harvested(CancellationToken ct) {
        var found = await binaries.GetExtractionCandidatesAsync(ct);
        return Ok(new {
            ok = found.Candidates.Count > 0,
            candidates = found.Candidates.Select(c => new {
                c.Platform,
                c.Version,
                bytes = c.Bytes.Length,
                symbols = c.Symbols?.Count ?? 0,
                diagnostics = c.Diagnostics
            }),
            rejected = found.Rejected
        });
    }

    private async Task<(bool Ok, byte[]? Bin, IReadOnlyList<MachoSymbols.Symbol>? Syms, string Source, string? Diag)>
        ResolveBinaryAsync(string? device, bool live, CancellationToken ct) {
        if (live) {
            var h = await binaries.GetExtractionBinaryAsync(ct);
            if (h.Ok && h.Bytes is not null) return (true, h.Bytes, h.Symbols, "harvested", h.Diagnostics);
            return (false, null, null, "harvested", h.Diagnostics);
        }

        (bool ok, byte[]? bin, string? diag) = await binaries.GetBinaryAsync(device, ct);
        return (ok, bin, null, "stash", diag);
    }

    [HttpGet("section")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> Section(
        [FromQuery] string va, [FromQuery] int count, [FromQuery] string elem = "f64",
        [FromQuery] string? device = null, [FromQuery] bool live = false, CancellationToken ct = default) {
        if (!TryParseVa(va, out ulong addr)) return Fail(400, "va must be hex (0x...) or decimal");
        if (!Arm64ConstSectionReader.TryParseElem(elem, out var elemType))
            return Fail(400, "elem must be one of f32,f64,i32,i64,u32,u64");
        try {
            (bool ok, byte[]? bin, _, string source, string? diag) = await ResolveBinaryAsync(device, live, ct);
            if (!ok || bin is null) return Ok(new { ok = false, diagnostics = diag });
            var dump = Arm64ConstSectionReader.Dump(bin, addr, count, elemType);
            return Ok(new {
                ok = dump.Ok,
                source,
                va = "0x" + dump.Va.ToString("x", CultureInfo.InvariantCulture),
                segment = dump.Segment,
                section = dump.Section,
                elem,
                values = dump.Values,
                diagnostics = dump.Diagnostics
            });
        } catch (Exception ex) {
            return Ok(new { ok = false, diagnostics = ex.Message });
        }
    }

    private static bool TryParseVa(string s, out ulong va) {
        va = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.TryParse(s.AsSpan(2), NumberStyles.HexNumber, null, out va)
            : ulong.TryParse(s, out va);
    }

    private OkObjectResult VaResult(
        byte[] tgt, SymbolRecovery.RecoveryReport report, string name, ulong va, ulong textVm, ulong textOff,
        string method, object? detail) {
        bool snapped = MachoFunctionStarts.TryEnclosingStart(tgt, va, out ulong startVa, out ulong endVa);
        ulong hookVa = snapped ? startVa : va;
        bool wasMidFunction = snapped && startVa != va;
        return Ok(new {
            ok = true,
            tier = report.Tier,
            recovered = report.Recovered,
            method,
            name,
            va = "0x" + va.ToString("x", CultureInfo.InvariantCulture),
            textVmAddr = "0x" + textVm.ToString("x", CultureInfo.InvariantCulture),
            textFileOff = "0x" + textOff.ToString("x", CultureInfo.InvariantCulture),
            rawTextOffset = "0x" + (va - textVm).ToString("x", CultureInfo.InvariantCulture),
            functionStartVa = "0x" + hookVa.ToString("x", CultureInfo.InvariantCulture),
            functionEndVa = snapped ? "0x" + endVa.ToString("x", CultureInfo.InvariantCulture) : null,
            hookOffset = "0x" + (hookVa - textVm).ToString("x", CultureInfo.InvariantCulture),
            wasMidFunction,
            detail
        });
    }

    private async Task<IActionResult> ExtractAsync(string[] needles, string? device, CancellationToken ct) {
        try {
            (bool ok, byte[]? bin, string? diag) = await binaries.GetBinaryAsync(device, ct);
            if (!ok || bin is null) return Ok(new { ok = false, diagnostics = diag });
            var res = FunctionConstantExtractor.Extract(bin, needles);
            return Ok(new { ok = res.Ok, result = Shape(res) });
        } catch (DllNotFoundException) {
            return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" });
        } catch (Exception ex) {
            return Ok(new { ok = false, diagnostics = ex.Message });
        }
    }

    private static object Shape(FunctionConstantExtractor.ExtractResult r) => new {
        ok = r.Ok,
        function = r.FunctionName,
        floats = r.Floats,
        calls = r.Calls,
        diagnostics = r.Diagnostics
    };
}
