using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.ValueObjects.Common;
using MicroKit.Persistence.IntegrationTests.PostgreSql;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// Proves that an aggregate deriving from <see cref="AuditableAggregateRoot{TId}"/> round-trips
/// through EF Core although <c>Id</c>, <c>CreatedAt</c> and <c>CreatedBy</c> have private
/// <c>init</c> accessors and <c>UpdatedAt</c>/<c>UpdatedBy</c> private setters (#166).
/// </summary>
/// <remarks>
/// The materialisation constructor passes placeholders for the creation fields, so the values
/// can only come back right if EF Core writes the base class's backing fields.
/// </remarks>
public static class AuditableAggregateRoundTrip
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = CreatedAt.AddMinutes(5);

    public static void Run(Func<LedgerDbContext> createContext)
    {
        var id = new LedgerId(Guid.CreateVersion7());
        var creator = new Actor("user-123");

        using (var context = createContext())
        {
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();

            var ledger = Ledger.Open(id, "draft", CreatedAt, creator);
            ledger.Rename("final", UpdatedAt, Actor.System);
            context.Ledgers.Add(ledger);
            context.SaveChanges();
        }

        using (var context = createContext())
        {
            var loaded = context.Ledgers.Single(l => l.Id == id);

            loaded.Id.ShouldBe(id);
            loaded.Name.ShouldBe("final");
            loaded.CreatedAt.ShouldBe(CreatedAt);
            loaded.CreatedBy.ShouldBe(creator);
            loaded.UpdatedAt.ShouldBe(UpdatedAt);
            loaded.UpdatedBy.ShouldBeSameAs(Actor.System);
        }
    }
}

public sealed class AuditableAggregateRoundTripSqliteTests
{
    [Fact]
    public void AuditableAggregate_RoundTripsThroughEfCore_OnSqlite()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        AuditableAggregateRoundTrip.Run(() => new LedgerDbContext(
            new DbContextOptionsBuilder<LedgerDbContext>().UseSqlite(connection).Options));
    }
}

[Collection(PostgreSqlSuite.Name)]
public sealed class AuditableAggregateRoundTripPostgreSqlTests(PostgreSqlFixture fixture)
{
    [DockerRequiredFact]
    public void AuditableAggregate_RoundTripsThroughEfCore_OnPostgreSql()
    {
        AuditableAggregateRoundTrip.Run(() => new LedgerDbContext(
            new DbContextOptionsBuilder<LedgerDbContext>().UseNpgsql(fixture.ConnectionString).Options));
    }
}

public readonly record struct LedgerId(Guid Value) : IEntityId;

public sealed class Ledger : AuditableAggregateRoot<LedgerId>
{
    // Used by EF Core only. The creation fields receive placeholders that EF Core then overwrites
    // through the backing fields, as the AuditableAggregateRoot constructor remarks describe.
    private Ledger(LedgerId id, string name)
        : base(id, DateTimeOffset.UnixEpoch, Actor.Migration)
    {
        Name = name;
    }

    // The unbindable last parameter keeps EF Core off this constructor: otherwise it would bind
    // createdAt and createdBy itself and the backing fields would go unexercised.
    private Ledger(LedgerId id, string name, DateTimeOffset createdAt, Actor createdBy, bool _)
        : base(id, createdAt, createdBy)
    {
        Name = name;
    }

    public string Name { get; private set; }

    public static Ledger Open(LedgerId id, string name, DateTimeOffset createdAt, Actor createdBy) =>
        new(id, name, createdAt, createdBy, true);

    public void Rename(string name, DateTimeOffset now, Actor actor)
    {
        Name = name;
        Touch(now, actor);
    }
}

public sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
{
    public DbSet<Ledger> Ledgers => Set<Ledger>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ledger>(b =>
        {
            b.ToTable("ledgers");
            b.HasKey(l => l.Id);
            b.Property(l => l.Id).HasConversion(id => id.Value, value => new LedgerId(value)).ValueGeneratedNever();
            b.Property(l => l.Name);
            b.Property(l => l.CreatedAt);
            b.Property(l => l.CreatedBy).HasConversion(a => a.Value, value => Actor.Restore(value));
            b.Property(l => l.UpdatedAt);
            b.Property(l => l.UpdatedBy).HasConversion(a => a!.Value, value => Actor.Restore(value));
            b.Ignore(l => l.DomainEvents);
        });
    }
}
