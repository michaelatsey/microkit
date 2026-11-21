using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace MicroKit.Messaging.IntegrationTests.Infrastructure;

/// <summary>A transient fault the test strategy retries, raised at a transaction's commit.</summary>
public sealed class InjectedCommitFaultException() : Exception("injected transient commit fault");

/// <summary>Where <see cref="CommitFaultInjector"/> strikes.</summary>
public enum CommitFaultPoint
{
    /// <summary>Nothing is injected.</summary>
    None,

    /// <summary>The commit never reaches the database: the transaction rolls back.</summary>
    BeforeCommit,

    /// <summary>The commit reached the database, then the fault is raised: its outcome looks unknown.</summary>
    AfterCommit,
}

/// <summary>
/// Raises <see cref="InjectedCommitFaultException"/> at the next transaction commits after
/// <see cref="Arm"/> — once by default — before or after the commit reaches the database.
/// </summary>
public sealed class CommitFaultInjector : DbTransactionInterceptor
{
    private CommitFaultPoint _point;
    private int _armed;
    private int _fired;

    /// <summary>Gets whether the fault was raised since the last <see cref="Arm"/>.</summary>
    public bool Fired => Volatile.Read(ref _fired) == 1;

    /// <summary>Arms a fault at each of the next <paramref name="times"/> commits.</summary>
    /// <param name="point">Where to strike.</param>
    /// <param name="times">How many commits fail.</param>
    public void Arm(CommitFaultPoint point, int times = 1)
    {
        _point = point;
        Volatile.Write(ref _fired, 0);
        Volatile.Write(ref _armed, point is CommitFaultPoint.None ? 0 : times);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        Strike(CommitFaultPoint.BeforeCommit);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Strike(CommitFaultPoint.AfterCommit);
        return Task.CompletedTask;
    }

    private void Strike(CommitFaultPoint point)
    {
        if (_point == point && Interlocked.Decrement(ref _armed) >= 0)
        {
            Volatile.Write(ref _fired, 1);
            throw new InjectedCommitFaultException();
        }
    }
}

/// <summary>
/// The provider's retrying strategy (what <c>EnableRetryOnFailure</c> installs), retrying
/// <see cref="InjectedCommitFaultException"/> besides the provider's own transient faults.
/// </summary>
public sealed class FaultRetryingStrategy(ExecutionStrategyDependencies dependencies)
    : NpgsqlRetryingExecutionStrategy(dependencies, 3, TimeSpan.FromMilliseconds(20), null)
{
    /// <inheritdoc />
    protected override bool ShouldRetryOn(Exception? exception)
        => exception is InjectedCommitFaultException or InjectedTransientReadException || base.ShouldRetryOn(exception);
}

/// <summary>A transient provider fault on a read: <see cref="DbException.IsTransient"/> is true.</summary>
public sealed class InjectedTransientReadException() : DbException("injected transient read fault")
{
    /// <inheritdoc />
    public override bool IsTransient => true;
}

/// <summary>
/// Fails the next inbox reads after <see cref="CommitFaultInjector"/> fired — the commit
/// verification — with a transient provider fault, <see cref="Arm"/>ed times.
/// </summary>
public sealed class VerificationReadFault(CommitFaultInjector commits) : DbCommandInterceptor
{
    private int _armed;

    /// <summary>Fails the next <paramref name="times"/> inbox reads after a commit fault.</summary>
    /// <param name="times">How many reads fail.</param>
    public void Arm(int times = 1) => Volatile.Write(ref _armed, times);

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (commits.Fired
            && command.CommandText.Contains("FROM \"InboxMessages\"", StringComparison.Ordinal)
            && Interlocked.Decrement(ref _armed) >= 0)
        {
            throw new InjectedTransientReadException();
        }

        return ValueTask.FromResult(result);
    }
}

/// <summary>Fails every INSERT of a <c>Failed</c> inbox row while armed.</summary>
public sealed class FailedRowInsertBlocker : DbCommandInterceptor
{
    private volatile bool _armed;

    /// <summary>Gets or sets whether the inserts fail.</summary>
    public bool Armed
    {
        get => _armed;
        set => _armed = value;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Check(command);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Check(command);
        return ValueTask.FromResult(result);
    }

    private void Check(DbCommand command)
    {
        if (_armed
            && command.CommandText.Contains("INSERT INTO \"InboxMessages\"", StringComparison.Ordinal)
            && command.Parameters.Cast<DbParameter>().Any(p => "Failed".Equals(p.Value)))
        {
            throw new TimeoutException("injected: the Failed inbox row cannot be written");
        }
    }
}
