namespace EggIncognito.Runner.Runners;

public interface IDeviceRunner {
    string Platform { get; }
    Task<RunOutcome> RunOnceAsync(bool force, CancellationToken ct = default);
}

public sealed record RunOutcome(bool Emitted, string? Build, string? ProtoSha, string Detail);
