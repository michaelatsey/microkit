namespace MicroKit.Messaging.MediatR.IntegrationTests.Infrastructure;

/// <summary>
/// The single aggregate used by every scenario in this suite.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a plain class implementing <see cref="IDomainEventsProvider"/> rather than
/// <c>AggregateRoot&lt;TId&gt;</c>: the latter constrains <c>TId : IEntityId</c>, which would drag a
/// strongly-typed identifier and its EF Core value converter into a harness whose subject is the
/// dispatch path, not identifier mapping. Precedent: <c>TestAggregate</c> in
/// <c>MicroKit.Persistence.IntegrationTests/EntityFrameworkCore/EfDomainEventsProviderTests.cs</c>.
/// </para>
/// <para>
/// What matters for the drain is the contract, not the base class: <c>EfDomainEventsProvider</c>
/// walks <c>ChangeTracker.Entries&lt;IHasDomainEvents&gt;()</c> and drains the entries that also
/// implement <see cref="IDomainEventsProvider"/> — which this type does, exactly as a real
/// aggregate root would.
/// </para>
/// <para>
/// One aggregate type is reused across all scenarios so the model needs exactly one
/// <c>Ignore(nameof(DomainEvents))</c> call; scenarios are distinguished by the event they raise.
/// </para>
/// </remarks>
internal sealed class E2EAggregate : IDomainEventsProvider
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>A mutable scalar so a scenario can reach the <c>Modified</c> state if it needs to.</summary>
    public string Name { get; set; } = string.Empty;

    public IReadOnlyList<IDomainEvent> DomainEvents =>
        _domainEvents.Count == 0 ? Array.Empty<IDomainEvent>() : _domainEvents.AsReadOnly();

    public void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public IReadOnlyList<IDomainEvent> DrainDomainEvents()
    {
        if (_domainEvents.Count == 0)
            return Array.Empty<IDomainEvent>();

        var drained = _domainEvents.ToArray();
        _domainEvents.Clear();
        return drained;
    }
}
