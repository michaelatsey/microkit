using System.Data.Common;
using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MicroKit.Persistence.IntegrationTests.PostgreSql;

/// <summary>A row the transaction tests write. The key is client-assigned, so a retry can re-insert it.</summary>
public sealed class ExecutionRow
{
    public Guid Id { get; init; }

    public string Name { get; set; } = string.Empty;
}

public sealed class ExecutionDbContext(DbContextOptions<ExecutionDbContext> options) : DbContext(options)
{
    public DbSet<ExecutionRow> Rows => Set<ExecutionRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ExecutionRow>(b =>
        {
            b.ToTable("execution_rows");
            b.Property(r => r.Id).ValueGeneratedNever();
        });
}

/// <summary>The exception a test classifies as transient — nothing else is.</summary>
public sealed class TransientMarkerException : Exception
{
    public TransientMarkerException() : base("Simulated transient failure.") { }

    public TransientMarkerException(string message) : base(message) { }

    public TransientMarkerException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// A retrying strategy built on EF Core's own <see cref="ExecutionStrategy"/>: the base class
/// unwraps <see cref="DbUpdateException"/> through <c>CallOnWrappedException</c> before
/// <see cref="ShouldRetryOn"/> sees the failure, exactly as the provider strategies do.
/// </summary>
public sealed class MarkerRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies, int maxRetryCount)
    : ExecutionStrategy(dependencies, maxRetryCount, TimeSpan.FromMilliseconds(1))
{
    protected override bool ShouldRetryOn(Exception exception) => exception is TransientMarkerException;

    // No back-off in tests: the base still decides when the retry budget is spent.
    protected override TimeSpan? GetNextDelay(Exception lastException) =>
        base.GetNextDelay(lastException) is null ? null : TimeSpan.Zero;
}

/// <summary>
/// Fails the first <c>failures</c> INSERT commands, by default with
/// <see cref="TransientMarkerException"/>. The failure happens inside <c>SaveChangesAsync</c>,
/// which wraps it in a <see cref="DbUpdateException"/> itself — the shape a real transient write
/// failure has.
/// </summary>
public sealed class FailingInsertInterceptor(int failures, Func<Exception>? failure = null) : DbCommandInterceptor
{
    private readonly Func<Exception> _failure = failure ?? (() => new TransientMarkerException());
    private int _remaining = failures;

    public int Thrown { get; private set; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        FailIfInsert(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        FailIfInsert(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void FailIfInsert(DbCommand command)
    {
        if (_remaining <= 0 || !command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase))
            return;

        _remaining--;
        Thrown++;
        throw _failure();
    }
}

/// <summary>Counts the transaction commands EF Core issues, to prove who committed or rolled back.</summary>
public sealed class TransactionCountingInterceptor : DbTransactionInterceptor
{
    public int Begun { get; private set; }

    public int Committed { get; private set; }

    public int RolledBack { get; private set; }

    public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default)
    {
        Begun++;
        return base.TransactionStartingAsync(connection, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        Committed++;
        return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult> TransactionRollingBackAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        RolledBack++;
        return base.TransactionRollingBackAsync(transaction, eventData, result, cancellationToken);
    }
}

/// <summary>Fails the first <c>failures</c> transaction COMMITs with the exception <c>failure</c> builds.</summary>
public sealed class FailingCommitInterceptor(int failures, Func<Exception> failure) : DbTransactionInterceptor
{
    private int _remaining = failures;

    public int Thrown { get; private set; }

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (_remaining > 0)
        {
            _remaining--;
            Thrown++;
            throw failure();
        }

        return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
    }
}

/// <summary>Makes every rollback fail before it reaches the database.</summary>
public sealed class FailingRollbackInterceptor : DbTransactionInterceptor
{
    public int Attempts { get; private set; }

    public override ValueTask<InterceptionResult> TransactionRollingBackAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        Attempts++;
        throw new InvalidOperationException("Simulated rollback failure.");
    }
}

/// <summary>
/// One throw-away PostgreSQL database per test, on the shared container: tests never see each
/// other's rows, and the round-trip tests' <c>EnsureDeleted</c> never drops this schema.
/// </summary>
public sealed class ExecutionDatabase : IAsyncDisposable
{
    private ExecutionDatabase(string connectionString) => ConnectionString = connectionString;

    public string ConnectionString { get; }

    public static async Task<ExecutionDatabase> CreateAsync(PostgreSqlFixture fixture)
    {
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = "exec_" + Guid.NewGuid().ToString("N"),
        };
        var database = new ExecutionDatabase(builder.ConnectionString);
        await using var context = database.CreateContext();
        await context.Database.EnsureCreatedAsync();
        return database;
    }

    /// <summary>Creates a context; a non-null <paramref name="maxRetryCount"/> installs the retrying strategy.</summary>
    public ExecutionDbContext CreateContext(int? maxRetryCount = null, params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
            {
                if (maxRetryCount is { } retries)
                    npgsql.ExecutionStrategy(d => new MarkerRetryingExecutionStrategy(d, retries));
            })
            .AddInterceptors(interceptors)
            .UseMicroKitPersistence()
            .Options;

        return new ExecutionDbContext(options);
    }

    public async Task<List<ExecutionRow>> ReadRowsAsync()
    {
        await using var context = CreateContext();
        return await context.Rows.AsNoTracking().OrderBy(r => r.Name).ToListAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}
