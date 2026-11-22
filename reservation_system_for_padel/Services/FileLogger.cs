
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace reservation_system_for_padel.Services
{
    public class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private static readonly object LockObject = new();

        private static readonly string LogDirectory =
            Path.Combine(AppContext.BaseDirectory, "Logs");

        private static readonly string LogFile =
            Path.Combine(LogDirectory, $"log-{DateTime.Now:yyyy-MM-dd}.txt");

        public FileLogger(string categoryName)
        {
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);

            var logEntry =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] " +
                $"[{logLevel}] [{_categoryName}] {message}";

            if (exception != null)
                logEntry += Environment.NewLine + exception;

            lock (LockObject)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(
                    LogFile,
                    logEntry + Environment.NewLine
                );
            }
        }
    }

    public class FileLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new FileLogger(categoryName);
        }

        public void Dispose() { }
    }
}
