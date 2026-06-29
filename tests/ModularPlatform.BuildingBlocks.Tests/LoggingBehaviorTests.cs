using Microsoft.Extensions.Logging;
using ModularPlatform.Cqrs;
using ModularPlatform.Cqrs.Behaviors;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class LoggingBehaviorTests
{
    [Fact]
    public async Task Successful_request_logs_type_and_elapsed_time_without_request_body()
    {
        var logger = new CapturingLogger<LoggingBehavior<SensitiveRequest, string>>();
        var behavior = new LoggingBehavior<SensitiveRequest, string>(logger);

        var result = await behavior.Handle(
            new SensitiveRequest("secret@example.com", "super-secret-token"),
            () => Task.FromResult("ok"),
            CancellationToken.None);

        result.ShouldBe("ok");
        var entry = logger.SingleEntry();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldContain(nameof(SensitiveRequest));
        entry.Message.ShouldContain("Handled");
        entry.Message.ShouldNotContain("secret@example.com");
        entry.Message.ShouldNotContain("super-secret-token");
    }

    [Fact]
    public async Task Business_exception_is_logged_as_warning_with_error_code()
    {
        var logger = new CapturingLogger<LoggingBehavior<SensitiveRequest, string>>();
        var behavior = new LoggingBehavior<SensitiveRequest, string>(logger);

        var ex = await Should.ThrowAsync<BusinessRuleException>(() =>
            behavior.Handle(
                new SensitiveRequest("secret@example.com", "super-secret-token"),
                () => throw new BusinessRuleException("test.rule_broken", "Rule broken."),
                CancellationToken.None));

        ex.ErrorCode.ShouldBe("test.rule_broken");
        var entry = logger.SingleEntry();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldContain(nameof(SensitiveRequest));
        entry.Message.ShouldContain("test.rule_broken");
        entry.Message.ShouldNotContain("secret@example.com");
        entry.Message.ShouldNotContain("super-secret-token");
    }

    [Fact]
    public async Task Unexpected_exception_is_logged_as_error_with_exception()
    {
        var logger = new CapturingLogger<LoggingBehavior<SensitiveRequest, string>>();
        var behavior = new LoggingBehavior<SensitiveRequest, string>(logger);
        var failure = new InvalidOperationException("boom");

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new SensitiveRequest("secret@example.com", "super-secret-token"),
                () => throw failure,
                CancellationToken.None));

        ex.ShouldBeSameAs(failure);
        var entry = logger.SingleEntry();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeSameAs(failure);
        entry.Message.ShouldContain(nameof(SensitiveRequest));
        entry.Message.ShouldContain("failed");
        entry.Message.ShouldNotContain("secret@example.com");
        entry.Message.ShouldNotContain("super-secret-token");
    }

    private sealed record SensitiveRequest(string Email, string AccessToken);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _entries = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }

        public LogEntry SingleEntry() => _entries.Single();
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
