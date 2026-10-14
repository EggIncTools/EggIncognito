using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Core.Services;

public sealed class EndpointStore(
    IEndpointSource fileSource,
    IServiceScopeFactory? scopeFactory,
    ILogger<EndpointStore> logger) : IEndpointStore {
    public TRes Fetch<TRes>(string path, string? eid = null) where TRes : IMessage<TRes>, new() {
        byte[]? bytes = LookupBytes(path, eid);
        return bytes is null ? new TRes() : JsonParser.Default.Parse<TRes>(Encoding.UTF8.GetString(bytes));
    }

    public IMessage Fetch(Type messageType, string path, string? eid = null) {
        var instance = (IMessage)Activator.CreateInstance(messageType)!;
        byte[]? bytes = LookupBytes(path, eid);
        return bytes is null ? instance : JsonParser.Default.Parse(Encoding.UTF8.GetString(bytes), instance.Descriptor);
    }

    public async Task<string?> CurrentJsonAsync(string route, CancellationToken ct) =>
        (await CurrentAsync(route, ct))?.Json;

    public Task<CurrentEndpoint?> CurrentAsync(string route, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Current(route));
    }

    private CurrentEndpoint? Current(string route) {
        if (scopeFactory is not null) {
            try {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetService<DbEndpointSourceMarker>()?.Source;
                if (db is not null) {
                    var (dbBytes, dbStamp) = db.TryLookupWithStamp(route, null);
                    byte[]? stripped = StripBom(dbBytes);
                    if (stripped is not null) return new CurrentEndpoint(Encoding.UTF8.GetString(stripped), dbStamp, "db");
                }
            } catch (Exception ex) {
                logger.LogDbEndpointLookupFailed(ex, route, null);
            }
        }

        var (fileBytes, fileStamp) = fileSource.TryLookupWithStamp(route, null);
        byte[]? fileStripped = StripBom(fileBytes);
        return fileStripped is null ? null : new CurrentEndpoint(Encoding.UTF8.GetString(fileStripped), fileStamp, "fixture");
    }

    internal byte[]? LookupBytes(string path, string? eid) {
        if (scopeFactory is not null) {
            try {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetService<DbEndpointSourceMarker>()?.Source;
                byte[]? hit = db?.Lookup(path, eid);
                if (hit is not null) return StripBom(hit);
            } catch (Exception ex) {
                logger.LogDbEndpointLookupFailed(ex, path, eid);
            }
        }

        return StripBom(fileSource.Lookup(path, eid));
    }

    private static byte[]? StripBom(byte[]? bytes) =>
        bytes is [0xEF, 0xBB, 0xBF, .. var rest] ? rest : bytes;
}

public sealed record CurrentEndpoint(string Json, DateTimeOffset? CapturedAt, string Origin);

public sealed class DbEndpointSourceMarker(IEndpointSource source) {
    public IEndpointSource Source => source;
}

internal static partial class EndpointStoreLog {
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "DB endpoint lookup failed for {Path} (eid {Eid}); using file default")]
    internal static partial void LogDbEndpointLookupFailed(this ILogger logger, Exception ex, string path, string? eid);
}
