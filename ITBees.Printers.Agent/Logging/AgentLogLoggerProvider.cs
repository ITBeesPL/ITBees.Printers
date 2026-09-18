using Microsoft.Extensions.Logging;

namespace ITBees.Printers.Agent.Logging;

/// <summary>
/// Routes the SignalR client's own log into the agent log file, as trace lines
/// (see <see cref="AgentInfo.TraceVariable"/>).
/// </summary>
public sealed class AgentLogLoggerProvider : ILoggerProvider
{
    private readonly AgentLog _log;
    private readonly string _source;

    public AgentLogLoggerProvider(AgentLog log, string source)
    {
        _log = log;
        _source = source;
    }

    public ILogger CreateLogger(string categoryName)
    {
        // "Microsoft.AspNetCore.Http.Connections.Client.Internal.WebSocketsTransport" -> "WebSocketsTransport"
        return new Logger(_log, $"{_source} {categoryName[(categoryName.LastIndexOf('.') + 1)..]}");
    }

    public void Dispose()
    {
    }

    private sealed class Logger : ILogger
    {
        private readonly AgentLog _log;
        private readonly string _prefix;

        public Logger(AgentLog log, string prefix)
        {
            _log = log;
            _prefix = prefix;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            for (var inner = exception; inner != null; inner = inner.InnerException)
            {
                message += $" [{inner.GetType().Name}: {inner.Message}]";
            }

            _log.Trace($"{_prefix} {eventId.Name}: {message}");
        }
    }
}
