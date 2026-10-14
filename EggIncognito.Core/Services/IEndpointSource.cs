namespace EggIncognito.Core.Services;

public interface IEndpointSource {
    int Priority { get; }
    byte[]? Lookup(string path, string? eid);

    (byte[]? Bytes, DateTimeOffset? Stamp) TryLookupWithStamp(string path, string? eid) =>
        (Lookup(path, eid), null);
}
