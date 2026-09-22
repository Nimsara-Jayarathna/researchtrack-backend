using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ResearchTrack.BuildingBlocks.Api.RuntimeLogging;

public sealed class RuntimeLogLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly RuntimeLogStore _store;
    private readonly string _serviceName;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    public RuntimeLogLoggerProvider(RuntimeLogStore store, IHostEnvironment environment)
    {
        _store = store;
        _serviceName = environment.ApplicationName;
    }

    public ILogger CreateLogger(string categoryName) =>
        new RuntimeLogger(categoryName, _serviceName, _store, () => _scopeProvider);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public void Dispose()
    {
    }

    private sealed class RuntimeLogger : ILogger
    {
        private static readonly string[] SensitivePropertyNames =
        [
            "password", "token", "secret", "authorization", "cookie", "api-key",
            "apikey", "connectionstring", "credential"
        ];

        private readonly string _category;
        private readonly string _serviceName;
        private readonly RuntimeLogStore _store;
        private readonly Func<IExternalScopeProvider> _scopeProvider;

        public RuntimeLogger(
            string category,
            string serviceName,
            RuntimeLogStore store,
            Func<IExternalScopeProvider> scopeProvider)
        {
            _category = category;
            _serviceName = serviceName;
            _store = store;
            _scopeProvider = scopeProvider;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            _scopeProvider().Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            AddProperties(properties, state);
            _scopeProvider().ForEachScope(
                static (scope, target) => AddProperties(target, scope),
                properties);

            if (GetString(properties, "RequestPath")?.StartsWith(
                    RuntimeLogEndpoints.RoutePrefix,
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }

            var message = formatter(state, exception);
            var exceptionText = exception?.ToString();

            foreach (var (key, value) in properties.ToArray())
            {
                if (IsSensitive(key))
                {
                    var sensitiveValue = value?.ToString();
                    properties[key] = "[REDACTED]";
                    if (!string.IsNullOrWhiteSpace(sensitiveValue) && sensitiveValue.Length >= 3)
                    {
                        message = message.Replace(sensitiveValue, "[REDACTED]", StringComparison.Ordinal);
                        exceptionText = exceptionText?.Replace(sensitiveValue, "[REDACTED]", StringComparison.Ordinal);
                    }
                }
                else
                {
                    properties[key] = NormalizeValue(value);
                }
            }

            var traceId = GetString(properties, "TraceId")
                ?? GetString(properties, "CorrelationId")
                ?? Activity.Current?.TraceId.ToString();

            _store.Add(new RuntimeLogEntry(
                0,
                DateTimeOffset.UtcNow,
                logLevel.ToString(),
                _category,
                eventId.Id,
                message,
                exceptionText,
                _serviceName,
                traceId,
                properties));
        }

        private static void AddProperties(IDictionary<string, object?> target, object? state)
        {
            if (state is not IEnumerable<KeyValuePair<string, object?>> values)
            {
                return;
            }

            foreach (var (key, value) in values)
            {
                if (!string.Equals(key, "{OriginalFormat}", StringComparison.Ordinal))
                {
                    target[key] = value;
                }
            }
        }

        private static bool IsSensitive(string name) => SensitivePropertyNames.Any(
            sensitiveName => name.Contains(sensitiveName, StringComparison.OrdinalIgnoreCase));

        private static object? NormalizeValue(object? value) => value switch
        {
            null => null,
            string or bool or byte or sbyte or short or ushort or int or uint or long or ulong
                or float or double or decimal or DateTime or DateTimeOffset or Guid => value,
            Enum enumValue => enumValue.ToString(),
            _ => value.ToString()
        };

        private static string? GetString(IReadOnlyDictionary<string, object?> properties, string key) =>
            properties.TryGetValue(key, out var value) ? value?.ToString() : null;
    }
}
