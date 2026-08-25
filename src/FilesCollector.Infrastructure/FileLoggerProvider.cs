using Microsoft.Extensions.Logging;

namespace FilesCollector.Infrastructure;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly object _syncRoot = new();
    private bool _disposed;

    public FileLoggerProvider(string logsDirectory, DateTimeOffset startedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);

        Directory.CreateDirectory(logsDirectory);
        foreach (var expiredPath in Directory.EnumerateFiles(logsDirectory, "files-collector-*.log")
                     .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                     .Skip(14))
        {
            try
            {
                File.Delete(expiredPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A log held open by another running instance must not prevent startup.
            }
        }

        var fileName = $"files-collector-{startedAt:yyyy-MM-dd}.log";
        var filePath = Path.Combine(logsDirectory, fileName);
        _writer = new StreamWriter(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read), new System.Text.UTF8Encoding(false))
        {
            AutoFlush = true
        };
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new FileLogger(categoryName, Write);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _writer.Dispose();
            _disposed = true;
        }
    }

    private void Write(LogLevel logLevel, string categoryName, EventId eventId, Exception? exception, string message)
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _writer.WriteLine($"{DateTimeOffset.Now:O}\t{logLevel}\t{categoryName}\t{eventId.Id}\t{message}");
            if (exception is not null)
            {
                _writer.WriteLine(exception);
            }
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly Action<LogLevel, string, EventId, Exception?, string> _write;

        public FileLogger(string categoryName, Action<LogLevel, string, EventId, Exception?, string> write)
        {
            _categoryName = categoryName;
            _write = write;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Information;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            _write(logLevel, _categoryName, eventId, exception, formatter(state, exception));
        }
    }
}
