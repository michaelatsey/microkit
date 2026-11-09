using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// MicroKit's only <c>SaveChanges</c> entry point, registered by <c>UseMicroKitPersistence()</c>:
/// it runs the aggregate save steps, in a fixed order, before EF Core writes anything.
/// </summary>
/// <remarks>
/// <para>
/// The order is fixed here, not left to the order in which EF Core runs interceptors: a step that
/// changes entry states must run before the steps that read them. #197 phase 4 inserts the
/// soft-delete conversion (a <c>Deleted</c> root becomes <c>Modified</c>) in front of
/// <see cref="AggregateVersionStep"/>, so a soft-deleted root is bumped like any modified one. The
/// version step is always last (ADR-010).
/// </para>
/// <para>
/// <c>SavingChanges</c> runs before EF Core's own <c>DetectChanges</c>: <see cref="AggregateSave"/>
/// detects the changes first, when automatic detection is on.
/// </para>
/// </remarks>
internal sealed class AggregateSaveInterceptor : SaveChangesInterceptor
{
    // The versions bumped by the save in flight, per context: a save that fails or is canceled writes
    // nothing, so its bumps are undone and GetVersion never reports a version that was not committed.
    private static readonly ConditionalWeakTable<DbContext, IReadOnlyList<VersionBump>> s_inFlight = new();

    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Run(eventData.Context);
        return result;
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Run(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc/>
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Forget(eventData?.Context);
        return result;
    }

    /// <inheritdoc/>
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Forget(eventData?.Context);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc/>
    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Undo(eventData?.Context);

    /// <inheritdoc/>
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Undo(eventData?.Context);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override void SaveChangesCanceled(DbContextEventData eventData) => Undo(eventData?.Context);

    /// <inheritdoc/>
    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        Undo(eventData?.Context);
        return Task.CompletedTask;
    }

    private static void Run(DbContext? context)
    {
        if (context is null)
            return;
        s_inFlight.Remove(context);
        if (AggregateSave.Begin(context) is not { } save)
            return;

        // The steps, in order: the soft-delete conversion turns deletes into updates, which the version
        // step then bumps like any other write.
        // The entities it marked are detached once a save succeeds, as EF Core detaches what a save deleted;
        // the list lives on the scoped record, so a cleared tracker or a pooled lease drops it.
        var softDeleted = AggregateSoftDeleteStep.Apply(save);
        if (softDeleted.Count > 0)
            save.Record?.SoftDeleted.AddRange(softDeleted.Select(e => e.Entity));

        var bumps = AggregateVersionStep.Apply(save);
        if (bumps.Count > 0)
            s_inFlight.AddOrUpdate(context, bumps);
    }

    private static void Forget(DbContext? context)
    {
        if (context is null)
            return;
        s_inFlight.Remove(context);
        if (KeyLookup.FindLoadRecord(context) is not { SoftDeleted.Count: > 0 } record)
            return;

        var softDeleted = new HashSet<object>(record.SoftDeleted, ReferenceEqualityComparer.Instance);
        record.SoftDeleted.Clear();
        foreach (var entity in softDeleted)
            record.Forget(entity);

        // Through the tracker's current entries: only what is still tracked is detached.
        foreach (var entry in context.ChangeTracker.Entries().Where(e => softDeleted.Contains(e.Entity)).ToList())
            entry.State = EntityState.Detached;
    }

    private static void Undo(DbContext? context)
    {
        if (context is not null && s_inFlight.TryGetValue(context, out var bumps))
        {
            s_inFlight.Remove(context);
            AggregateVersionStep.Undo(bumps);
        }
    }
}
