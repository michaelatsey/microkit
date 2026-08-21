using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.MediatR.IntegrationTests.Infrastructure;

/// <summary>One captured log record, flattened to the fields a test can assert on.</summary>
internal readonly record struct CapturedLogEntry(
    LogLevel Level,
    int EventId,
    string CategoryName,
    string RenderedMessage,
    string? ExceptionTypeName)
{
    /// <summary>A single-line form suitable for verbatim reporting.</summary>
    public override string ToString()
        => ExceptionTypeName is null
            ? $"[{Level}] {CategoryName}: {RenderedMessage}"
            : $"[{Level}] {CategoryName}: {RenderedMessage} ({ExceptionTypeName})";
}

/// <summary>
/// An in-memory <see cref="ILoggerProvider"/> that captures every log record in order.
/// Test infrastructure only — no production code depends on it.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddLogging()</c> with no provider registered discards everything, so without this the
/// processor's retry and dead-letter decisions are invisible. In the redelivery scenario the log
/// line is the only place the swallowed-or-escaped question shows up at all: the row state alone
/// cannot distinguish "the store absorbed the duplicate" from "the exception escaped and the
/// processor caught it".
/// </para>
/// <para>
/// Written rather than reused because the monorepo has no logger double to reuse: zero
/// <see cref="ILoggerProvider"/> implementations, and <c>Microsoft.Extensions.Diagnostics.Testing</c>
/// (<c>FakeLogger</c>) is not in Central Package Management.
/// </para>
/// </remarks>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    /// <summary>Everything captured so far, in the order it was logged.</summary>
    public IReadOnlyList<CapturedLogEntry> Entries => [.. _entries];

    /// <summary>
    /// The current entry count, to be passed to <see cref="Since"/> after the next drain so each
    /// drain's output is attributable to that drain alone.
    /// </summary>
    public int Mark() => _entries.Count;

    /// <summary>Entries captured after <paramref name="mark"/>.</summary>
    public IReadOnlyList<CapturedLogEntry> Since(int mark) => [.. _entries.Skip(mark)];

    /// <summary>Entries at or above <paramref name="level"/>, captured after <paramref name="mark"/>.</summary>
    public IReadOnlyList<CapturedLogEntry> Since(int mark, LogLevel level)
        => [.. _entries.Skip(mark).Where(e => e.Level >= level)];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    // Deliberately a no-op: the ILoggerFactory owns and disposes registered providers, and the
    // captured entries must stay readable after the ServiceProvider is disposed.
    public void Dispose() { }

    private sealed class CapturingLogger(string categoryName, ConcurrentQueue<CapturedLogEntry> sink)
        : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // MUST be true. Returning false short-circuits every [LoggerMessage]-generated call before
        // it ever reaches Log, and OutboxProcessor additionally guards its debug line with an
        // explicit IsEnabled(LogLevel.Debug) check. The pre-existing double in
        // MicroKit.MediatR.UnitTests/Behaviors/LoggingBehaviorTests.cs returns false, which is
        // precisely why it captures no records.
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => sink.Enqueue(new CapturedLogEntry(
                logLevel,
                eventId.Id,
                categoryName,
                formatter(state, exception),
                exception?.GetType().Name));
    }
}
