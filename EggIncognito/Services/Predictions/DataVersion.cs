namespace EggIncognito.Services.Predictions;

public abstract class DataVersion {
    private long _version;
    public long Version => Interlocked.Read(ref _version);
    public void Bump() => Interlocked.Increment(ref _version);
}

public sealed class EventDataVersion : DataVersion;

public sealed class ContractDataVersion : DataVersion;
