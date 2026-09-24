using Microsoft.Extensions.Logging;

namespace EggIncognito.Tests;

public sealed class CollectingLogger<T> : ILogger<T> {
    public List<string> Messages { get; } = [];
    public List<LogLevel> Levels { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) {
        Messages.Add(formatter(state, exception));
        Levels.Add(logLevel);
    }
}
