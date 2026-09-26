using System.Reflection;
using FilesCollector.App;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

/// <summary>Regression tests for B6: an ordinary UI exception must not close the application.</summary>
public sealed class UnhandledExceptionPolicyTests
{
    private DateTimeOffset _now = new(2026, 9, 25, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Ordinary_exceptions_keep_the_application_running()
    {
        var policy = CreatePolicy();

        policy.Decide(new NullReferenceException()).Should().Be(UnhandledExceptionAction.Continue);
        policy.Decide(new InvalidOperationException()).Should().Be(UnhandledExceptionAction.Continue);
    }

    [Fact]
    public void Fatal_exceptions_shut_down_even_when_wrapped()
    {
        var policy = CreatePolicy();

        policy.Decide(new OutOfMemoryException()).Should().Be(UnhandledExceptionAction.Shutdown);
        policy.Decide(new TargetInvocationException(new AggregateException(new InvalidProgramException()))).Should().Be(UnhandledExceptionAction.Shutdown);
        UnhandledExceptionPolicy.IsFatal(new TypeInitializationException("Demo", null)).Should().BeTrue();
        UnhandledExceptionPolicy.IsFatal(new AggregateException(new IOException(), new IOException())).Should().BeFalse();
    }

    [Fact]
    public void A_burst_of_errors_shuts_down_to_avoid_an_endless_dialog_loop()
    {
        var policy = CreatePolicy();

        policy.Decide(new InvalidOperationException()).Should().Be(UnhandledExceptionAction.Continue);
        _now += TimeSpan.FromSeconds(5);
        policy.Decide(new InvalidOperationException()).Should().Be(UnhandledExceptionAction.Continue);
        _now += TimeSpan.FromSeconds(5);
        policy.Decide(new InvalidOperationException()).Should().Be(UnhandledExceptionAction.Shutdown);
    }

    [Fact]
    public void Errors_older_than_the_window_are_forgotten()
    {
        var policy = CreatePolicy();

        for (var index = 0; index < 10; index++)
        {
            policy.Decide(new InvalidOperationException()).Should().Be(UnhandledExceptionAction.Continue);
            _now += TimeSpan.FromSeconds(31);
        }
    }

    private UnhandledExceptionPolicy CreatePolicy()
    {
        return new UnhandledExceptionPolicy(3, TimeSpan.FromSeconds(30), () => _now);
    }
}
