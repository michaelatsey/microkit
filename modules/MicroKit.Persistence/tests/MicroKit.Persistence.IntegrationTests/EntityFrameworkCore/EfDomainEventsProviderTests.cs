using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MicroKit.Domain.Events;
using MicroKit.Persistence.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.EntityFrameworkCore;

public sealed class EfDomainEventsProviderTests
{
    [Fact]
    public void DrainDomainEvents_WithSeveralTrackedAggregates_DrainsAllInOneCall()
    {
        using var context = CreateContext();
        var first = new TestAggregate();
        var second = new TestAggregate();
        first.Raise(new TestEvent("first-a"));
        first.Raise(new TestEvent("first-b"));
        second.Raise(new TestEvent("second-a"));
        context.Add(first);
        context.Add(second);
        var sut = new EfDomainEventsProvider<TestDbContext>(context);

        var drained = sut.DrainDomainEvents();

        drained.Count.ShouldBe(3);
        drained.OfType<TestEvent>().Select(e => e.Name)
            .ShouldBe(["first-a", "first-b", "second-a"], ignoreOrder: true);
    }

    [Fact]
    public void DrainDomainEvents_WhenNoAggregateHasPendingEvents_ReturnsEmpty()
    {
        using var context = CreateContext();
        context.Add(new TestAggregate());
        context.Add(new TestAggregate());
        var sut = new EfDomainEventsProvider<TestDbContext>(context);

        var drained = sut.DrainDomainEvents();

        drained.ShouldBeEmpty();
    }

    [Fact]
    public void DrainDomainEvents_IgnoresTrackedEntitiesExposingNoEvents()
    {
        using var context = CreateContext();
        var aggregate = new TestAggregate();
        aggregate.Raise(new TestEvent("only-one"));
        context.Add(aggregate);
        context.Add(new TestEntityWithoutEvents());
        context.Add(new TestAggregate());
        var sut = new EfDomainEventsProvider<TestDbContext>(context);

        var drained = sut.DrainDomainEvents();

        drained.Count.ShouldBe(1);
        drained[0].ShouldBeOfType<TestEvent>().Name.ShouldBe("only-one");
    }

    [Fact]
    public void DrainDomainEvents_SkipsEntityThatExposesEventsButCannotDrain()
    {
        using var context = CreateContext();
        var aggregate = new TestAggregate();
        aggregate.Raise(new TestEvent("drainable"));
        var exposeOnly = new TestExposeOnly();
        exposeOnly.Raise(new TestEvent("not-drainable"));
        context.Add(aggregate);
        context.Add(exposeOnly);
        var sut = new EfDomainEventsProvider<TestDbContext>(context);

        // The documented asymmetry: the read member reports everything exposed...
        sut.DomainEvents.Count.ShouldBe(2);

        // ...the drain takes only what implements IDomainEventsProvider.
        var drained = sut.DrainDomainEvents();

        drained.Count.ShouldBe(1);
        drained[0].ShouldBeOfType<TestEvent>().Name.ShouldBe("drainable");
        exposeOnly.DomainEvents.Count.ShouldBe(1, "an undrainable entity must be left untouched");
    }

    [Fact]
    public void DrainDomainEvents_LeavesDrainedAggregateEmpty()
    {
        using var context = CreateContext();
        var aggregate = new TestAggregate();
        aggregate.Raise(new TestEvent("once"));
        context.Add(aggregate);
        var sut = new EfDomainEventsProvider<TestDbContext>(context);

        sut.DrainDomainEvents().Count.ShouldBe(1);
        var second = sut.DrainDomainEvents();

        second.ShouldBeEmpty("a drained aggregate must not dispatch twice");
        aggregate.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void DomainEvents_ReadsWithoutClearing()
    {
        using var context = CreateContext();
        var aggregate = new TestAggregate();
        aggregate.Raise(new TestEvent("kept"));
        context.Add(aggregate);
        var sut = new EfDomainEventsProvider<TestDbContext>(context);

        sut.DomainEvents.Count.ShouldBe(1);
        sut.DomainEvents.Count.ShouldBe(1, "reading must not clear");

        sut.DrainDomainEvents().Count.ShouldBe(1, "the events survived both reads");
    }

    [Fact]
    public void AddUnitOfWork_RegistersDomainEventsProvider()
    {
        var services = new ServiceCollection();
        services.AddMicroKitPersistence(persistence =>
            persistence.AddEntityFrameworkCore(ef => ef
                .AddDbContext<TestDbContext>(o => o.UseSqlite(InMemoryConnectionString))
                .AddUnitOfWork<TestDbContext>()));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetRequiredService<IDomainEventsProvider>();

        resolved.ShouldBeOfType<EfDomainEventsProvider<TestDbContext>>();
    }

    // The connection is never opened: the model is built, nothing is executed.
    private const string InMemoryConnectionString = "DataSource=:memory:";

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(InMemoryConnectionString)
            .Options;

        return new TestDbContext(options);
    }
}

public sealed record TestEvent(string Name) : DomainEvent;

/// <summary>Stands in for an aggregate: exposes events and can drain them.</summary>
public sealed class TestAggregate : IDomainEventsProvider
{
    private readonly List<IDomainEvent> _events = [];

    public Guid Id { get; init; } = Guid.NewGuid();

    public IReadOnlyList<IDomainEvent> DomainEvents =>
        _events.Count == 0 ? Array.Empty<IDomainEvent>() : _events.AsReadOnly();

    public void Raise(IDomainEvent domainEvent) => _events.Add(domainEvent);

    public IReadOnlyList<IDomainEvent> DrainDomainEvents()
    {
        if (_events.Count == 0)
            return Array.Empty<IDomainEvent>();

        var drained = _events.ToArray();
        _events.Clear();
        return drained;
    }
}

/// <summary>Exposes events via the read contract but offers no way to drain them.</summary>
public sealed class TestExposeOnly : IHasDomainEvents
{
    private readonly List<IDomainEvent> _events = [];

    public Guid Id { get; init; } = Guid.NewGuid();

    public IReadOnlyList<IDomainEvent> DomainEvents => _events.AsReadOnly();

    public void Raise(IDomainEvent domainEvent) => _events.Add(domainEvent);
}

/// <summary>A tracked entity with no domain-event contract at all.</summary>
public sealed class TestEntityWithoutEvents
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<TestAggregate> Aggregates => Set<TestAggregate>();

    public DbSet<TestExposeOnly> ExposeOnly => Set<TestExposeOnly>();

    public DbSet<TestEntityWithoutEvents> Plain => Set<TestEntityWithoutEvents>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // DomainEvents is a domain concern, never a mapped navigation.
        modelBuilder.Entity<TestAggregate>().Ignore(nameof(TestAggregate.DomainEvents));
        modelBuilder.Entity<TestExposeOnly>().Ignore(nameof(TestExposeOnly.DomainEvents));
        modelBuilder.Entity<TestEntityWithoutEvents>();
    }
}
