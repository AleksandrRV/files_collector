using System.Reflection;
using System.Runtime.InteropServices;

namespace FilesCollector.App;

public enum UnhandledExceptionAction
{
    /// <summary>Report the error and keep the application running.</summary>
    Continue,

    /// <summary>Report the error and shut the application down.</summary>
    Shutdown
}

/// <summary>
/// Decides whether an unhandled UI exception is recoverable.
/// </summary>
/// <remarks>
/// A failed command (a file that cannot be opened, an unexpected value in a handler)
/// should not close the application and lose unsaved presets, so by default the error
/// is reported and the application continues. It shuts down when the exception means the
/// process state can no longer be trusted (<see cref="IsFatal"/>), or when errors keep
/// coming (<paramref name="maxErrorsInWindow"/> within <paramref name="window"/>): that is
/// usually an exception raised on every layout or timer tick, and continuing would only
/// show the same dialog again and again.
/// </remarks>
public sealed class UnhandledExceptionPolicy
{
    private readonly int _maxErrorsInWindow;
    private readonly TimeSpan _window;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Queue<DateTimeOffset> _recentErrors = new();
    private readonly object _syncRoot = new();

    public UnhandledExceptionPolicy(int maxErrorsInWindow = 3, TimeSpan? window = null, Func<DateTimeOffset>? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxErrorsInWindow, 1);
        _maxErrorsInWindow = maxErrorsInWindow;
        _window = window ?? TimeSpan.FromSeconds(30);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public UnhandledExceptionAction Decide(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (IsFatal(exception))
        {
            return UnhandledExceptionAction.Shutdown;
        }

        lock (_syncRoot)
        {
            var now = _clock();
            while (_recentErrors.Count > 0 && now - _recentErrors.Peek() > _window)
            {
                _recentErrors.Dequeue();
            }

            _recentErrors.Enqueue(now);
            return _recentErrors.Count >= _maxErrorsInWindow
                ? UnhandledExceptionAction.Shutdown
                : UnhandledExceptionAction.Continue;
        }
    }

    /// <summary>
    /// Exceptions after which the runtime or a type is left in an unusable state. Wrapper
    /// exceptions are unwrapped first.
    /// </summary>
    public static bool IsFatal(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var current = Unwrap(exception);
        return current is OutOfMemoryException
            or InsufficientExecutionStackException
            or InvalidProgramException
            or BadImageFormatException
            or TypeInitializationException
            or SEHException
            or AccessViolationException;
    }

    private static Exception Unwrap(Exception exception)
    {
        var current = exception;
        while (true)
        {
            switch (current)
            {
                case TargetInvocationException { InnerException: { } inner }:
                    current = inner;
                    continue;
                case AggregateException aggregate when aggregate.InnerExceptions.Count == 1:
                    current = aggregate.InnerExceptions[0];
                    continue;
                default:
                    return current;
            }
        }
    }
}
