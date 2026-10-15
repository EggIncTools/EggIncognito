namespace EggIncognito.Services.Predictions;

public abstract class DataVersion {
    private long _version;
    public event Action? Changed;
    public long Version => Interlocked.Read(ref _version);

    public void Bump() {
        Interlocked.Increment(ref _version);
        Changed?.Invoke();
    }
}

public sealed class EventDataVersion : DataVersion;

public sealed class ContractDataVersion : DataVersion;
