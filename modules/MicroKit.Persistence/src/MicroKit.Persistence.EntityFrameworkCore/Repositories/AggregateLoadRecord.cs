using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// The aggregate roots <see cref="EfRepository{TAggregate, TId, TContext}"/> loaded whole into this
/// context, recorded by reference. A root the change tracker holds but this record does not was
/// tracked some other way, possibly partially, and the write side refuses it.
/// </summary>
/// <remarks>
/// <para>What enters the record, and what leaves it:</para>
/// <list type="bullet">
///   <item><description>a root the repository's load returned, once the load has completed, and only
///     if it was tracked <see cref="EntityTrackedEventArgs.FromQuery"/> during that load
///     (<see cref="Complete"/>): a root tracked earlier and handed back by identity resolution, a root
///     the model auto-included without its own aggregate, and a root of a load that failed midway
///     are never recorded;</description></item>
///   <item><description>a root staged by the repository's <c>AddAsync</c> (<see cref="RecordAdded"/>):
///     the application built it whole, so it stays recorded once the commit makes it
///     <c>Unchanged</c>;</description></item>
///   <item><description>any other <see cref="ChangeTracker.Tracked"/> event of a root — an
///     <c>Attach</c>, <c>Update</c> or <c>Add</c>, a reattachment after
///     <see cref="ChangeTracker.Clear"/>, a tracking query outside the repository's load — removes
///     it;</description></item>
///   <item><description>a root whose state changes to <see cref="EntityState.Detached"/> leaves
///     it.</description></item>
/// </list>
/// <para>
/// <see cref="ChangeTracker.Clear"/> raises no event, so whoever clears the tracker on MicroKit's
/// behalf (<see cref="EfUnitOfWork{TContext}"/>) calls <see cref="Reset"/> too. A root recorded,
/// cleared by the application itself and reattached is still caught: the reattachment raises
/// <see cref="ChangeTracker.Tracked"/>.
/// </para>
/// <para>
/// Pooling: returning a context to its pool resets the state manager, which drops every
/// change-tracker event handler, and resets this record (<see cref="IResettableService"/>). Each
/// <see cref="BeginLoad"/> and <see cref="RecordAdded"/> therefore subscribe again, idempotently.
/// Between a reset and the next load, an attach is not observed, but the record is empty then:
/// nothing recorded can be attached unseen.
/// </para>
/// </remarks>
internal sealed class AggregateLoadRecord(ICurrentDbContext currentContext) : IResettableService
{
    // Weak: ChangeTracker.Clear() raises no event, so after an application-level Clear() a strong set
    // would keep every graph loaded before it alive for the context's lifetime (a batch job clearing
    // between batches would leak). Keys are compared by reference. Reset() remains the cleanup on the
    // paths MicroKit owns. The candidates are strong but live only for one load scope.
    private readonly ConditionalWeakTable<object, object> _recorded = new();
    private static readonly object Marker = new();
    private readonly HashSet<object> _candidates = new(ReferenceEqualityComparer.Instance);
    private IEntityType? _loading;

    /// <summary>
    /// Opens the scope of one repository load of <paramref name="root"/>: a root of that hierarchy
    /// tracked from the query until the scope is disposed is a candidate, recorded only by
    /// <see cref="Complete"/>.
    /// </summary>
    internal LoadScope BeginLoad(IEntityType root)
    {
        Subscribe();
        _candidates.Clear();
        _loading = root;
        return new LoadScope(this);
    }

    /// <summary>
    /// Records the roots a completed load returned, among those it tracked itself. Called inside the
    /// load scope, after the query has materialised everything.
    /// </summary>
    internal void Complete(IEnumerable<object> returned)
    {
        foreach (var root in returned)
        {
            if (_candidates.Contains(root))
                _recorded.AddOrUpdate(root, Marker);
        }
    }

    /// <summary>Records a root the repository staged for insertion: it is whole by construction.</summary>
    internal void RecordAdded(object root)
    {
        Subscribe();
        _recorded.AddOrUpdate(root, Marker);
    }

    /// <summary>Gets a value indicating whether <paramref name="root"/> was loaded whole.</summary>
    internal bool IsRecorded(object root) => _recorded.TryGetValue(root, out _);

    /// <summary>Forgets every recorded root: the change tracker was cleared.</summary>
    internal void Reset()
    {
        _recorded.Clear();
        _candidates.Clear();
        _loading = null;
    }

    /// <inheritdoc/>
    public void ResetState() => Reset();

    /// <inheritdoc/>
    public Task ResetStateAsync(CancellationToken cancellationToken = default)
    {
        Reset();
        return Task.CompletedTask;
    }

    private void OnTracked(object? sender, EntityTrackedEventArgs e)
    {
        var entity = e.Entry.Entity;
        if (entity is not IAggregateRoot)
            return;

        if (e.FromQuery && _loading is not null && _loading.IsAssignableFrom(e.Entry.Metadata))
        {
            _candidates.Add(entity);
        }
        else
        {
            _recorded.Remove(entity);
            _candidates.Remove(entity);
        }
    }

    private void OnStateChanged(object? sender, EntityStateChangedEventArgs e)
    {
        if (e.NewState == EntityState.Detached && e.Entry.Entity is IAggregateRoot)
        {
            _recorded.Remove(e.Entry.Entity);
            _candidates.Remove(e.Entry.Entity);
        }
    }

    // Idempotent: a pooled context's handlers are dropped when it returns to the pool.
    private void Subscribe()
    {
        var tracker = currentContext.Context.ChangeTracker;
        tracker.Tracked -= OnTracked;
        tracker.Tracked += OnTracked;
        tracker.StateChanged -= OnStateChanged;
        tracker.StateChanged += OnStateChanged;
    }

    /// <summary>Closes a load scope; candidates not completed are forgotten.</summary>
    internal readonly struct LoadScope(AggregateLoadRecord record) : IDisposable
    {
        public void Dispose()
        {
            record._loading = null;
            record._candidates.Clear();
        }
    }
}
