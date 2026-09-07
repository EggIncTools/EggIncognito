using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EggIncognito.Core.Services.Devices;

public sealed class BridgeProcessRunner(IHttpClientFactory httpFactory, DeviceTransportConfig cfg) : IProcessRunner {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct) {
        (var plan, var body, string? failure) = await SendAsync(exe, args, false, ct);
        if (body is null || plan is null) return new ProcessResult(-1, "", failure ?? "bridge exec failed");

        string note = WriteOutputs(plan, body);
        return new ProcessResult(body.Exit, Text(body.Stdout), Join(Text(body.Stderr), note));
    }

    public async Task<ProcessBytesResult> RunBytesAsync(string exe, string[] args, CancellationToken ct) {
        (var plan, var body, string? failure) = await SendAsync(exe, args, true, ct);
        if (body is null || plan is null) return new ProcessBytesResult(-1, [], failure ?? "bridge exec failed");

        string note = WriteOutputs(plan, body);
        byte[] stdout;
        try {
            stdout = body.StdoutBase64 is { Length: > 0 } raw ? Convert.FromBase64String(raw) : [];
        } catch (FormatException ex) {
            return new ProcessBytesResult(-1, [], $"bridge exec returned unreadable stdout: {ex.Message}");
        }

        return new ProcessBytesResult(body.Exit, stdout, Join(Text(body.Stderr), note));
    }

    public async Task<ProcessHandle> StartAsync(string exe, string[] args, CancellationToken ct) {
        var client = Client();
        if (client.ConfigurationNote is { } missing) return ProcessHandle.Failed(missing);

        var plan = Plan(exe, args, true);
        HttpResponseMessage? resp = null;
        try {
            using var req = client.Build(HttpMethod.Post, BridgeRoutes.ExecStream);
            req.Content = await ContentAsync(plan, ct);
            resp = await client.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
                return ProcessHandle.Failed($"bridge exec/stream {(int)resp.StatusCode} {resp.ReasonPhrase}");

            var handle = Pump(resp, await resp.Content.ReadAsStreamAsync(ct));
            resp = null;
            return handle;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return ProcessHandle.Failed($"bridge exec/stream error: {BridgeClient.Describe(ex)}");
        } catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) {
            return ProcessHandle.Failed($"bridge exec/stream error: {BridgeClient.Describe(ex)}");
        } finally {
            resp?.Dispose();
        }
    }

    private BridgeClient Client() => new(httpFactory.CreateClient(BridgeClient.HttpClientName), cfg);

    private async Task<(ExecPlan? Plan, BridgeExecResult? Body, string? Failure)> SendAsync(
        string exe, string[] args, bool binaryStdout, CancellationToken ct) {
        var client = Client();
        if (client.ConfigurationNote is { } missing) return (null, null, missing);

        var plan = Plan(exe, args, binaryStdout);
        try {
            using var req = client.Build(HttpMethod.Post, BridgeRoutes.Exec);
            req.Content = await ContentAsync(plan, ct);
            using var resp = await client.Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return (plan, null, $"bridge exec {(int)resp.StatusCode} {resp.ReasonPhrase}");

            var body = await resp.Content.ReadFromJsonAsync<BridgeExecResult>(JsonOptions, ct);
            return body is null ? (plan, null, "bridge exec empty response") : (plan, body, null);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return (plan, null, $"bridge exec error: {BridgeClient.Describe(ex)}");
        } catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) {
            return (plan, null, $"bridge exec error: {BridgeClient.Describe(ex)}");
        }
    }

    private static ExecPlan Plan(string exe, string[] args, bool binaryStdout) {
        var mapped = new List<string>(args.Length);
        var inputs = new List<ExecFile>();
        var outputs = new List<ExecFile>();

        for (int i = 0; i < args.Length; i++) {
            string arg = args[i];
            string name = BridgePlaceholders.Name(i);
            if (File.Exists(arg)) {
                inputs.Add(new ExecFile(name, arg));
                mapped.Add(BridgePlaceholders.In(name));
            } else if (DeviceShell.IsTempPath(arg)) {
                outputs.Add(new ExecFile(name, arg));
                mapped.Add(BridgePlaceholders.Out(name));
            } else {
                mapped.Add(arg);
            }
        }

        var spec = new BridgeExecSpec(exe, mapped, [.. outputs.Select(o => o.Name)], null, binaryStdout);
        return new ExecPlan(spec, inputs, outputs);
    }

    private static async Task<MultipartFormDataContent> ContentAsync(ExecPlan plan, CancellationToken ct) {
        var content = new MultipartFormDataContent();
        string json = JsonSerializer.Serialize(plan.Spec, JsonOptions);
        content.Add(new StringContent(json, Encoding.UTF8, "application/json"), BridgeExecParts.Spec);

        foreach (var input in plan.Inputs) {
            var part = new ByteArrayContent(await File.ReadAllBytesAsync(input.Path, ct));
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(part, input.Name, Path.GetFileName(input.Path));
        }

        return content;
    }

    private static string WriteOutputs(ExecPlan plan, BridgeExecResult body) {
        if (plan.Outputs.Count == 0) return "";

        var returned = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var o in body.Outputs ?? []) returned[o.Name] = o.Base64;

        var failures = new List<string>();
        foreach (var output in plan.Outputs) {
            if (!returned.TryGetValue(output.Name, out string? base64)) continue;
            try {
                File.WriteAllBytes(output.Path, Convert.FromBase64String(base64));
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) {
                failures.Add($"{output.Path}: {ex.Message}");
            }
        }

        return failures.Count == 0 ? "" : "bridge output write failed: " + string.Join("; ", failures);
    }

    private static ProcessHandle Pump(HttpResponseMessage resp, Stream body) {
        var pipe = new Pipe();
        var exit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tail = new Tail();
        var cts = new CancellationTokenSource();

        _ = Task.Run(() => ReadFramesAsync(body, pipe.Writer, tail, exit, cts.Token), CancellationToken.None);

        return new ProcessHandle(pipe.Reader.AsStream(), exit.Task, tail.Snapshot, async () => {
            await cts.CancelAsync();
            await pipe.Writer.CompleteAsync();
            await body.DisposeAsync();
            resp.Dispose();
            cts.Dispose();
        });
    }

    private static async Task ReadFramesAsync(
        Stream body, PipeWriter writer, Tail tail, TaskCompletionSource<int> exit, CancellationToken ct) {
        string? failure = null;
        try {
            while (true) {
                var frame = await BridgeStreamFrames.ReadAsync(body, ct);
                if (frame is not { } f) {
                    failure = "the bridge stream ended before the exit frame";
                    break;
                }

                if (f.Kind == BridgeStreamFrames.Exit) {
                    exit.TrySetResult(f.ExitCode);
                    break;
                }

                if (f.Kind == BridgeStreamFrames.Stderr) {
                    tail.Append(Encoding.UTF8.GetString(f.Payload));
                    continue;
                }

                if (f.Payload.Length == 0) continue;
                var flush = await writer.WriteAsync(f.Payload, ct);
                if (flush.IsCompleted) break;
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            failure = $"the bridge stream broke: {BridgeClient.Describe(ex)}";
        } catch (OperationCanceledException) {
            failure = null;
        } finally {
            if (failure is { Length: > 0 }) tail.Append(failure);
            await writer.CompleteAsync();
            exit.TrySetResult(-1);
        }
    }

    private static string Text(string? s) => s ?? "";

    private static string Join(string stderr, string note) {
        if (note.Length == 0) return stderr;
        return stderr.Length == 0 ? note : stderr.TrimEnd() + "\n" + note;
    }

    private sealed record ExecFile(string Name, string Path);

    private sealed record ExecPlan(
        BridgeExecSpec Spec, IReadOnlyList<ExecFile> Inputs, IReadOnlyList<ExecFile> Outputs);

    private sealed class Tail {
        private const int Keep = 4096;
        private readonly Lock _gate = new();
        private readonly StringBuilder _text = new();

        public string Snapshot() {
            lock (_gate) return _text.ToString().Trim();
        }

        public void Append(string chunk) {
            lock (_gate) {
                _text.Append(chunk);
                if (_text.Length > Keep) _text.Remove(0, _text.Length - Keep);
            }
        }
    }
}
