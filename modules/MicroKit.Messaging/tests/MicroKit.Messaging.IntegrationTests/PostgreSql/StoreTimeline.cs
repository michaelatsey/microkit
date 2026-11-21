using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>One timed thing a store's context did: a connection, a command or a transaction.</summary>
internal readonly record struct StoreActivity(string Store, DateTimeOffset Start, DateTimeOffset End, string What)
{
    public bool Overlaps(DateTimeOffset from, DateTimeOffset to) => Start <= to && End >= from;
}

/// <summary>One sample of the thread pool, and how late the sampler itself woke.</summary>
internal readonly record struct PoolSample(DateTimeOffset At, TimeSpan Lateness, int Threads, long Pending);

/// <summary>
/// Records what each store's context did, with timestamps, so a missed latency bound in V5 says
/// what the other store — and the thread pool — were doing at that moment.
/// </summary>
/// <remarks>
/// One interceptor per context, named after its store. Every connection opening (and its failure),
/// every command and every transaction end is recorded with its start and end, which is what lets
/// a late message be laid against the other store's work: an overlap names a shared resource, no
/// overlap rules one out.
/// </remarks>
internal sealed partial class StoreTimeline
{
    private readonly ConcurrentQueue<StoreActivity> _activities = new();
    private readonly ConcurrentQueue<PoolSample> _pool = new();

    public IReadOnlyList<StoreActivity> Activities => [.. _activities];

    public IReadOnlyList<PoolSample> Pool => [.. _pool];

    public IInterceptor For(string store) => new Interceptor(store, _activities);

    /// <summary>Samples the thread pool every <paramref name="period"/> until cancelled.</summary>
    public async Task SamplePoolAsync(TimeSpan period, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(period, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var now = clock.Elapsed;
            _pool.Enqueue(new PoolSample(
                DateTimeOffset.UtcNow, now - last - period, ThreadPool.ThreadCount, ThreadPool.PendingWorkItemCount));
            last = now;
        }
    }

    /// <summary>A command's verb and table, e.g. <c>UPDATE OutboxMessages</c>.</summary>
    internal static string Describe(string sql)
    {
        var verb = sql.TrimStart().Split((char[])[' ', '\n', '\r', '\t'], 2)[0].ToUpperInvariant();
        var table = TableRegex().Match(sql);
        return table.Success ? $"{verb} {table.Groups[1].Value}" : verb;
    }

    [GeneratedRegex("""\b(?:FROM|UPDATE|INTO)\s+"?(\w+)"?""", RegexOptions.IgnoreCase)]
    private static partial Regex TableRegex();

    private sealed class Interceptor(string store, ConcurrentQueue<StoreActivity> sink)
        : IDbConnectionInterceptor, IDbCommandInterceptor, IDbTransactionInterceptor
    {
        private void Add(DateTimeOffset start, TimeSpan duration, string what)
            => sink.Enqueue(new StoreActivity(store, start, start + duration, what));

        public Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken ct = default)
        {
            Add(eventData.StartTime, eventData.Duration, "open");
            return Task.CompletedTask;
        }

        public Task ConnectionFailedAsync(DbConnection connection, ConnectionErrorEventData eventData, CancellationToken ct = default)
        {
            Add(eventData.StartTime, eventData.Duration, $"open failed ({eventData.Exception.GetType().Name})");
            return Task.CompletedTask;
        }

        public ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken ct = default)
        {
            Add(eventData.StartTime, eventData.Duration, Describe(command.CommandText));
            return ValueTask.FromResult(result);
        }

        public ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken ct = default)
        {
            Add(eventData.StartTime, eventData.Duration, Describe(command.CommandText));
            return ValueTask.FromResult(result);
        }

        public ValueTask<object?> ScalarExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken ct = default)
        {
            Add(eventData.StartTime, eventData.Duration, Describe(command.CommandText));
            return ValueTask.FromResult(result);
        }

        public Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken ct = default)
        {
            Add(eventData.StartTime, eventData.Duration,
                $"{Describe(command.CommandText)} failed ({eventData.Exception.GetType().Name})");
            return Task.CompletedTask;
        }

        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken ct = default)
        {
            Add(eventData.StartTime, eventData.Duration, "commit");
            return Task.CompletedTask;
        }

        public Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken ct = default)
        {
            Add(eventData.StartTime, eventData.Duration, $"transaction {eventData.Action} failed ({eventData.Exception.GetType().Name})");
            return Task.CompletedTask;
        }
    }
}

/// <summary>Formats V5's evidence: latencies against the bound, and what overlapped each late one.</summary>
internal static class StoreIsolationReport
{
    /// <summary>EF Core's <c>ExecutionStrategyRetrying</c> event: one strategy retry.</summary>
    public const int StrategyRetrying = 10406;

    private static readonly IFormatProvider Inv = System.Globalization.CultureInfo.InvariantCulture;

    public static string Ms(TimeSpan t) => string.Create(Inv, $"{t.TotalMilliseconds:F0}ms");

    /// <summary>
    /// The phase's evidence. Each late message is laid against store B's activity, B's loop
    /// failures and strategy retries, A's own activity and the thread pool over its
    /// commit-to-handled window.
    /// </summary>
    public static string Phase(
        string phase,
        IReadOnlyList<(Guid Id, DateTimeOffset Committed, DateTimeOffset Handled)> samples,
        TimeSpan bound,
        string boundDerivation,
        StoreTimeline timeline,
        IReadOnlyList<CapturedLogEntry> logs,
        string storeB)
    {
        var text = new StringBuilder();
        var latencies = samples.Select(s => s.Handled - s.Committed).ToList();
        text.AppendLine(Inv, $"V5|phase={phase}|n={samples.Count}|bound={Ms(bound)}|max={Ms(latencies.Max())}" +
                        $"|late={latencies.Count(l => l > bound)}");
        text.AppendLine(Inv, $"  bound: {boundDerivation}");
        text.AppendLine(Inv, $"  A latencies (commit order): {string.Join(", ", latencies.Select(Ms))}");

        foreach (var late in samples.Where(s => s.Handled - s.Committed > bound))
        {
            var (from, to) = (late.Committed, late.Handled);
            var b = timeline.Activities.Where(a => a.Store == "B" && a.Overlaps(from, to)).ToList();
            var a = timeline.Activities.Where(x => x.Store == "A" && x.Overlaps(from, to)).ToList();
            var bLoop = logs.Where(e => e.At >= from && e.At <= to && e.EventId == 1401 && Value(e, "Store") == storeB).ToList();
            var retries = logs.Where(e => e.At >= from && e.At <= to && e.EventId == StrategyRetrying).ToList();
            var bSettles = b.Count(x => x.What.StartsWith("commit", StringComparison.Ordinal)
                                        || x.What.StartsWith("transaction", StringComparison.Ordinal));
            var pool = timeline.Pool.Where(p => p.At >= from && p.At <= to).ToList();

            text.AppendLine(string.Create(
                Inv,
                $"V5LATE|phase={phase}|id={late.Id}|latency={Ms(to - from)}|bActivities={b.Count}|bSettlements={bSettles}" +
                $"|bLoopFailures={bLoop.Count}|strategyRetries={retries.Count}" +
                $"|poolMaxLateness={(pool.Count == 0 ? "n/a" : Ms(pool.Max(p => p.Lateness)))}" +
                $"|poolMaxPending={(pool.Count == 0 ? "n/a" : pool.Max(p => p.Pending).ToString(Inv))}"));
            text.AppendLine(Inv, $"  window {from:HH:mm:ss.fff} → {to:HH:mm:ss.fff}");
            foreach (var x in b)
            {
                text.AppendLine(Inv, $"    B {x.Start:HH:mm:ss.fff}+{Ms(x.End - x.Start)} {x.What}");
            }

            foreach (var e in bLoop)
            {
                text.AppendLine(Inv, $"    B loop {e.At:HH:mm:ss.fff} {Value(e, "Loop")} failed, delay {Value(e, "Delay")}");
            }

            foreach (var e in retries)
            {
                text.AppendLine(Inv, $"    strategy retry {e.At:HH:mm:ss.fff} {e.RenderedMessage}");
            }

            foreach (var x in a)
            {
                text.AppendLine(Inv, $"    A {x.Start:HH:mm:ss.fff}+{Ms(x.End - x.Start)} {x.What}");
            }

            foreach (var p in pool.Where(p => p.Lateness > TimeSpan.FromMilliseconds(20) || p.Pending > 0))
            {
                text.AppendLine(Inv, $"    pool {p.At:HH:mm:ss.fff} late {Ms(p.Lateness)}, threads {p.Threads}, pending {p.Pending}");
            }
        }

        return text.ToString();
    }

    /// <summary>B's loop during the outage: each failed cycle with its delay, and each strategy retry.</summary>
    public static string LoopTimeline(IReadOnlyList<CapturedLogEntry> logs, string storeB)
    {
        var text = new StringBuilder("  B loop timeline:");
        text.AppendLine();
        foreach (var e in logs.Where(e => (e.EventId == 1401 && Value(e, "Store") == storeB) || e.EventId == StrategyRetrying))
        {
            text.AppendLine(e.EventId == 1401
                ? string.Create(Inv, $"    {e.At:HH:mm:ss.fff} {Value(e, "Loop")} failed, back-off {Value(e, "Delay")} ({e.ExceptionTypeName})")
                : string.Create(Inv, $"    {e.At:HH:mm:ss.fff} strategy retry: {e.RenderedMessage}"));
        }

        return text.ToString();
    }

    private static string? Value(CapturedLogEntry entry, string key)
        => entry.State.FirstOrDefault(kv => kv.Key == key).Value?.ToString();
}
