using Fms.Domain.Logging;
using Microsoft.Extensions.Logging;

namespace Fms.HttpApi.Host.Logging;

public sealed class RuntimeLogLoggerProvider : ILoggerProvider
{
    private readonly ILogSink _sink;

    public RuntimeLogLoggerProvider(ILogSink sink)
    {
        _sink = sink;
    }

    public ILogger CreateLogger(string categoryName) => new RuntimeLogger(categoryName, _sink);

    public void Dispose()
    {
    }

    private sealed class RuntimeLogger : ILogger
    {
        private readonly string _category;
        private readonly ILogSink _sink;

        public RuntimeLogger(string category, ILogSink sink)
        {
            _category = category;
            _sink = sink;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || formatter == null) return;
            if (_category.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                _category.StartsWith("System.", StringComparison.Ordinal) ||
                _category.Contains("Logging", StringComparison.Ordinal))
                return;
            if (logLevel < LogLevel.Warning && !_category.StartsWith("Fms.", StringComparison.Ordinal)) return;

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message)) return;
            string? payload = null;
            if (state is IReadOnlyList<KeyValuePair<string, object?>> values && values.Count > 0)
            {
                payload = System.Text.Json.JsonSerializer.Serialize(values.ToDictionary(item => item.Key, item => item.Value?.ToString()));
            }

            _sink.Enqueue(new LogWrite
            {
                Category = LogCategory.Runtime,
                Level = logLevel.ToString(),
                Source = _category,
                Actor = "SYSTEM",
                Message = exception == null ? message : message + " " + exception.Message,
                PayloadJson = payload,
                Success = logLevel < LogLevel.Error
            });
        }
    }
}
