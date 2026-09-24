using System.Diagnostics;

namespace EggIncognito.Core.Services.Devices;

public sealed class DeviceFlowRunner(IDeviceUiDriver ui) {
    public async Task<DeviceFlowResult> RunAsync(
        DeviceTarget target, IReadOnlyList<DeviceFlowStep> steps, Func<string, Task>? progress, CancellationToken ct) {
        var log = new List<string>();
        var fields = new Dictionary<string, string>();
        var shots = new List<DeviceFlowShot>();

        async Task EmitAsync(string line) {
            log.Add(line);
            await progress.ReportAsync(line);
        }

        foreach (var step in steps) {
            ct.ThrowIfCancellationRequested();
            string descriptor = Describe(step);

            async Task<DeviceFlowResult?> FailAsync(string? note) {
                if (step.Required) return new DeviceFlowResult(false, log, fields, shots, descriptor);
                await EmitAsync($"(optional) {descriptor} failed: {note}");
                return null;
            }

            switch (step.Kind) {
                case DeviceFlowStepKind.LaunchApp: {
                        await EmitAsync(descriptor);
                        var r = await ui.LaunchAppAsync(target, step.AppRef!, ct);
                        if (!r.Ok) {
                            var fail = await FailAsync(r.Note);
                            if (fail is not null) return fail;
                        }

                        break;
                    }
                case DeviceFlowStepKind.Tap: {
                        await EmitAsync(descriptor);
                        var r = await ui.TapAsync(target, step.Selector!, ct);
                        if (!r.Ok) {
                            var fail = await FailAsync(r.Note);
                            if (fail is not null) return fail;
                        }

                        break;
                    }
                case DeviceFlowStepKind.TapPoint: {
                        await EmitAsync(descriptor);
                        var r = await ui.TapPointAsync(target, step.X ?? 0, step.Y ?? 0, ct);
                        if (!r.Ok) {
                            var fail = await FailAsync(r.Note);
                            if (fail is not null) return fail;
                        }

                        break;
                    }
                case DeviceFlowStepKind.Swipe: {
                        await EmitAsync(descriptor);
                        var r = await ui.SwipeAsync(target, step.X ?? 0, step.Y ?? 0, step.X2 ?? 0, step.Y2 ?? 0,
                            step.DurationMs, ct);
                        if (!r.Ok) {
                            var fail = await FailAsync(r.Note);
                            if (fail is not null) return fail;
                        }

                        break;
                    }
                case DeviceFlowStepKind.Key: {
                        await EmitAsync(descriptor);
                        var r = await ui.KeyAsync(target, step.Key!.Value, ct);
                        if (!r.Ok) {
                            var fail = await FailAsync(r.Note);
                            if (fail is not null) return fail;
                        }

                        break;
                    }
                case DeviceFlowStepKind.InputText: {
                        await EmitAsync(descriptor);
                        var r = await ui.InputTextAsync(target, step.Text!, ct);
                        if (!r.Ok) {
                            var fail = await FailAsync(r.Note);
                            if (fail is not null) return fail;
                        }

                        break;
                    }
                case DeviceFlowStepKind.Sleep: {
                        await EmitAsync(descriptor);
                        await Task.Delay(TimeSpan.FromSeconds(step.TimeoutSeconds), ct);
                        break;
                    }
                case DeviceFlowStepKind.WaitForSelector: {
                        await EmitAsync($"{descriptor} (up to {step.TimeoutSeconds}s)");
                        var (ok, note) = await WaitAsync(target, step,
                            tree => UiSelector.Resolve(tree, step.Selector!) is not null, ct);
                        if (!ok) {
                            var fail = await FailAsync(note);
                            if (fail is not null) return fail;
                        }

                        break;
                    }
                case DeviceFlowStepKind.WaitForText: {
                        await EmitAsync($"{descriptor} (up to {step.TimeoutSeconds}s)");
                        var alternatives = step.Text!.Split(" OR ", StringSplitOptions.None);
                        var (ok, note) = await WaitAsync(target, step,
                            tree => tree.Nodes().Any(n => n.Text is not null &&
                                alternatives.Any(a => n.Text.Contains(a, StringComparison.Ordinal))), ct);
                        if (!ok) {
                            var fail = await FailAsync(note);
                            if (fail is not null) return fail;
                        }

                        break;
                    }
                case DeviceFlowStepKind.WaitForTextGone: {
                        await EmitAsync($"{descriptor} (up to {step.TimeoutSeconds}s)");
                        var (ok, note) = await WaitAsync(target, step,
                            tree => !tree.Nodes().Any(n =>
                                n.Text is not null && n.Text.Contains(step.Text!, StringComparison.Ordinal)), ct);
                        if (!ok) {
                            var fail = await FailAsync(note);
                            if (fail is not null) return fail;
                        }

                        break;
                    }
                case DeviceFlowStepKind.Screenshot: {
                        var r = await ui.ScreenshotAsync(target, ct);
                        if (r is { Ok: true, Value: { } png }) {
                            shots.Add(new DeviceFlowShot(step.Label!, png));
                            await EmitAsync($"screenshot {step.Label} ({png.Length} bytes)");
                        } else {
                            await EmitAsync($"screenshot {step.Label} failed: {r.Note}");
                        }

                        break;
                    }
                case DeviceFlowStepKind.AssertText: {
                        await EmitAsync(descriptor);
                        var dump = await ui.DumpAsync(target, ct);
                        bool found = dump is { Ok: true, Value: { } tree } && tree.Nodes().Any(n =>
                            n.Text is not null && n.Text.Contains(step.Text!, StringComparison.Ordinal));
                        if (!found) {
                            var fail = await FailAsync(dump.Ok ? "text not found" : dump.Note);
                            if (fail is not null) return fail;
                        }

                        break;
                    }
                case DeviceFlowStepKind.ReadField: {
                        var dump = await ui.DumpAsync(target, ct);
                        if (dump is not { Ok: true, Value: { } tree }) {
                            if (step.Required) await EmitAsync($"read field {step.FieldName} failed: {dump.Note}");
                            var fail = await FailAsync(dump.Note);
                            if (fail is not null) return fail;
                            break;
                        }

                        var node = UiSelector.Resolve(tree, step.Selector!);
                        if (node is null) {
                            if (step.Required) await EmitAsync($"read field {step.FieldName} failed: selector not found");
                            var fail = await FailAsync("selector not found");
                            if (fail is not null) return fail;
                            break;
                        }

                        string? value = !string.IsNullOrEmpty(node.Text) ? node.Text : NextNonEmptyText(tree, node);
                        if (value is null) {
                            if (step.Required) await EmitAsync($"read field {step.FieldName} failed: no text found");
                            var fail = await FailAsync("no text found");
                            if (fail is not null) return fail;
                            break;
                        }

                        fields[step.FieldName!] = value;
                        await EmitAsync($"read field {step.FieldName} = {value}");
                        break;
                    }
            }
        }

        return new DeviceFlowResult(true, log, fields, shots, null);
    }

    private async Task<(bool Ok, string? Note)> WaitAsync(
        DeviceTarget target, DeviceFlowStep step, Func<UiTree, bool> predicate, CancellationToken ct) {
        long start = Stopwatch.GetTimestamp();
        while (true) {
            var dump = await ui.DumpAsync(target, ct);
            if (dump is { Ok: true, Value: { } tree } && predicate(tree)) return (true, null);
            string? note = dump.Ok ? "condition not met" : dump.Note;
            if (Stopwatch.GetElapsedTime(start).TotalSeconds >= step.TimeoutSeconds) return (false, note);
            await Task.Delay(TimeSpan.FromSeconds(step.PollSeconds), ct);
        }
    }

    private static string Describe(DeviceFlowStep step) => step.Kind switch {
        DeviceFlowStepKind.LaunchApp => $"launch {step.AppRef}",
        DeviceFlowStepKind.Tap => $"tap {DescribeSelector(step.Selector!)}",
        DeviceFlowStepKind.TapPoint => $"tap point ({step.X},{step.Y})",
        DeviceFlowStepKind.Swipe => $"swipe ({step.X},{step.Y}) to ({step.X2},{step.Y2}) over {step.DurationMs}ms",
        DeviceFlowStepKind.WaitForSelector => $"wait selector {DescribeSelector(step.Selector!)}",
        DeviceFlowStepKind.WaitForText => $"wait text '{step.Text}'",
        DeviceFlowStepKind.WaitForTextGone => $"wait text gone '{step.Text}'",
        DeviceFlowStepKind.Key => $"key {step.Key}",
        DeviceFlowStepKind.InputText => $"input text '{step.Text}'",
        DeviceFlowStepKind.Sleep => $"sleep {step.TimeoutSeconds}s",
        DeviceFlowStepKind.Screenshot => $"screenshot {step.Label}",
        DeviceFlowStepKind.AssertText => $"assert text '{step.Text}'",
        DeviceFlowStepKind.ReadField => $"read field {step.FieldName}",
        _ => step.Kind.ToString()
    };

    private static string DescribeSelector(UiSelector selector) => $"{selector.By}={selector.Value}";

    private static string? NextNonEmptyText(UiTree tree, UiNode node) {
        var nodes = tree.Nodes().ToList();
        int index = nodes.FindIndex(n => ReferenceEquals(n, node));
        if (index < 0) return null;
        for (int i = index + 1; i < nodes.Count; i++) {
            if (!string.IsNullOrEmpty(nodes[i].Text)) return nodes[i].Text;
        }

        return null;
    }
}
