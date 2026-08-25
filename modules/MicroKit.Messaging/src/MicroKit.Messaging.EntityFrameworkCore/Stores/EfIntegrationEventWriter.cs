namespace MicroKit.Messaging.EntityFrameworkCore;

/// <summary>EF Core staging writer for integration events.</summary>
/// <remarks>
/// <para>
/// Deliberately trivial, and deliberately without a <c>SaveChangesAsync</c>. The row is tracked as
/// added and commits with whatever transaction the caller is already inside.
/// </para>
/// <para>
/// The mirror image of <c>EfOutboxStore.AddAsync</c>, which carries the same instruction in a
/// comment: stage the row, the caller's unit of work commits it. Neither owns a commit. That is
/// the rule the whole pipeline runs on, and the one place it would be easy to break by adding a
/// save "so the row is definitely written" — which would publish events for facts that later roll
/// back.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The application's <see cref="DbContext"/> type.</typeparam>
internal sealed class EfIntegrationEventWriter<TContext>(TContext context) : IIntegrationEventWriter
    where TContext : DbContext
{
    /// <inheritdoc/>
    public ValueTask AddAsync(IntegrationEventMessage message, CancellationToken ct = default)
    {
        context.Set<IntegrationEventMessage>().Add(message);

        // No SaveChangesAsync. The caller's unit of work owns the boundary.
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reflects the context <i>this writer</i> holds. If the caller opens its transaction on a
    /// different <c>DbContext</c> instance, the guard passes while the row commits somewhere else —
    /// the same scope-identity assumption <c>IInboxSettlementStore</c> rests on, and the reason
    /// that identity is pinned by an integration test rather than trusted to a comment.
    /// </remarks>
    public bool HasOpenTransaction => context.Database.CurrentTransaction is not null;
}
