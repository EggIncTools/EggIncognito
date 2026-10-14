using Microsoft.Extensions.Logging;

namespace EggIncognito.Core.Services;

public sealed class FileEndpointSource : IEndpointSource {
    private readonly Dictionary<string, byte[]> _endpoints = [with(StringComparer.OrdinalIgnoreCase)];
    private readonly Dictionary<string, DateTimeOffset> _stamps = [with(StringComparer.OrdinalIgnoreCase)];

    public FileEndpointSource(string endpointsPath, ILogger<FileEndpointSource>? logger = null) {
        if (!Directory.Exists(endpointsPath)) return;

        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (string file in Directory.EnumerateFiles(endpointsPath, "*.json", opts)) {
            string relative = Path.GetRelativePath(endpointsPath, file).Replace('\\', '/').Replace(".json", "");
            try {
                _endpoints[relative] = File.ReadAllBytes(file);
                _stamps[relative] = File.GetLastWriteTimeUtc(file);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                logger?.LogEndpointFileUnreadable(ex, file);
            }
        }
    }

    public int Count => _endpoints.Count;
    public int Priority => 0;

    public byte[]? Lookup(string path, string? eid) => TryLookupWithStamp(path, eid).Bytes;

    public (byte[]? Bytes, DateTimeOffset? Stamp) TryLookupWithStamp(string path, string? eid) {
        string cleanPath = path.TrimEnd('/');
        while (true) {
            if (eid is not null && _endpoints.TryGetValue($"eids/{eid}/{cleanPath}", out byte[]? eidBytes)) {
                _stamps.TryGetValue($"eids/{eid}/{cleanPath}", out var eidStamp);
                return (eidBytes, eidStamp);
            }

            if (_endpoints.TryGetValue($"default/{cleanPath}", out byte[]? defaultBytes)) {
                _stamps.TryGetValue($"default/{cleanPath}", out var defaultStamp);
                return (defaultBytes, defaultStamp);
            }

            int lastSlash = cleanPath.LastIndexOf('/');
            int firstSlash = cleanPath.IndexOf('/');
            if (lastSlash <= firstSlash) break;
            cleanPath = cleanPath[..lastSlash];
        }

        return (null, null);
    }
}

internal static partial class FileEndpointSourceLog {
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Endpoint fixture {File} could not be read; it will not be served")]
    internal static partial void LogEndpointFileUnreadable(this ILogger logger, Exception ex, string file);
}
