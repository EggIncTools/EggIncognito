using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public sealed class FakeRunner(Func<string, string[], ProcessResult> fn) : IProcessRunner {
    public List<(string exe, string[] args)> Calls { get; } = [];

    public Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct) {
        Calls.Add((exe, args));
        return Task.FromResult(fn(exe, args));
    }
}
