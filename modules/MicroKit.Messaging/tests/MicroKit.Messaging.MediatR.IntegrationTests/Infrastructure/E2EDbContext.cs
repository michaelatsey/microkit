using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MicroKit.Messaging.MediatR.IntegrationTests.Infrastructure;

/// <summary>
/// The one <see cref="DbContext"/> this suite runs against. No existing context in the monorepo
/// satisfies all three requirements below at once, hence a new one.
/// </summary>
internal sealed class E2EDbContext(DbContextOptions<E2EDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<E2EAggregate> Aggregates => Set<E2EAggregate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // (a) The outbox/inbox tables live in the consumer's own context — that is what makes the
        //     outbox row commit in the SAME transaction as the aggregate (ADR-MSG-002).
        modelBuilder.ApplyMessagingConfiguration();

        // (c) DomainEvents is a domain concern, never a mapped navigation.
        //     Precedent: EfDomainEventsProviderTests.TestDbContext.
        modelBuilder.Entity<E2EAggregate>().Ignore(nameof(E2EAggregate.DomainEvents));
    }

    // (b) SQLite has no native DateTimeOffset type, so range comparisons (<, <=, >, >=) are
    //     untranslatable. The outbox eligibility query in GetPendingAsync filters on
    //     LockedUntilUtc and NextRetryAtUtc with exactly those operators, so without a binary
    //     (ticks) conversion every drain would throw instead of returning rows.
    //     BOTH DateTimeOffset and DateTimeOffset? must be converted — the nullable columns
    //     (LockedUntilUtc, NextRetryAtUtc, ProcessedAtUtc) are the ones the filter touches.
    //     Precedent: TestMessagingDbContext.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder
            .Properties<DateTimeOffset>()
            .HaveConversion<DateTimeOffsetToBinaryConverter>();
        configurationBuilder
            .Properties<DateTimeOffset?>()
            .HaveConversion<DateTimeOffsetToBinaryConverter>();
    }
}
