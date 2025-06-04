using Microsoft.Extensions.Logging;

namespace LMSupplyDepots.External.LLamaEngine;

internal static class LoggerExtensions
{
    public static ILogger<T> GetLogger<T>(this ILogger logger) where T : class
    {
        return new TypedLogger<T>(logger);
    }

    private class TypedLogger<T> : ILogger<T> where T : class
    {
        private readonly ILogger _logger;

        public TypedLogger(ILogger logger)
        {
            _logger = logger;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => _logger.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel)
            => _logger.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _logger.Log(logLevel, eventId, state, exception, formatter);
    }
}