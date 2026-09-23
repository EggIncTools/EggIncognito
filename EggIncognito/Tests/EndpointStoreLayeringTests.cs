using System.Text;
using EggIncognito.Core.Services;
using Ei;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public class EndpointStoreLayeringTests {
    private static EndpointStore Store(IEndpointSource file, IEndpointSource? db) {
        IServiceScopeFactory? factory = null;
        if (db is not null) {
            var services = new ServiceCollection();
            services.AddScoped(_ => new DbEndpointSourceMarker(db));
            factory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        }

        return new EndpointStore(file, factory, NullLogger<EndpointStore>.Instance);
    }

    [Fact]
    public void Db_Overrides_File_ForSameKey() {
        var file = new FakeSource(new Dictionary<string, string> { ["ei/x"] = "{}" }, 0);
        var db = new FakeSource(new Dictionary<string, string> { ["ei/x"] = "{\"userId\":\"FROM_DB\"}" }, 100);
        var store = Store(file, db);
        var msg = store.Fetch<AuthenticatedMessage>("ei/x");
        Assert.Equal("FROM_DB", msg.UserId);
    }

    [Fact]
    public void NonGeneric_Get_AppliesDbOverlay() {
        var file = new FakeSource(new Dictionary<string, string> { ["ei/x"] = "{}" }, 0);
        var db = new FakeSource(new Dictionary<string, string> { ["ei/x"] = "{\"userId\":\"FROM_DB\"}" }, 100);
        var store = Store(file, db);
        var msg = (AuthenticatedMessage)store.Fetch(typeof(AuthenticatedMessage), "ei/x");
        Assert.Equal("FROM_DB", msg.UserId);
    }

    [Fact]
    public void Db_Throws_FallsBackToFileDefault_AndLogsWarning() {
        var file = new FakeSource(new Dictionary<string, string> { ["ei/x"] = "{\"userId\":\"FROM_FILE\"}" }, 0);
        var services = new ServiceCollection();
        services.AddScoped(_ => new DbEndpointSourceMarker(new ThrowingSource()));
        var factory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var logger = new CollectingLogger();
        var store = new EndpointStore(file, factory, logger);

        var msg = store.Fetch<AuthenticatedMessage>("ei/x");

        Assert.Equal("FROM_FILE", msg.UserId);
        Assert.Contains(LogLevel.Warning, logger.Levels);
    }

    private sealed class FakeSource(Dictionary<string, string> map, int prio) : IEndpointSource {
        public int Priority => prio;

        public byte[]? Lookup(string path, string? eid)
            => map.TryGetValue(eid is null ? path : $"{eid}:{path}", out string? v) ? Encoding.UTF8.GetBytes(v) : null;
    }

    private sealed class ThrowingSource : IEndpointSource {
        public int Priority => 100;
        public byte[]? Lookup(string path, string? eid) => throw new InvalidOperationException("db down");
    }

    private sealed class CollectingLogger : ILogger<EndpointStore> {
        public List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }
}
