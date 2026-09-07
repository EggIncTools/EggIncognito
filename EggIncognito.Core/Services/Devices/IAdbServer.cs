namespace EggIncognito.Core.Services.Devices;

public interface IAdbServer {
    string Socket { get; }
    bool Owned { get; }
    string Describe();
    Task RestartAsync(CancellationToken ct);
}
