using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MicroKit.Messaging.IntegrationTests.Infrastructure;

public sealed class TestMessagingDbContext(DbContextOptions<TestMessagingDbContext> options)
    : MessagingTestDbContext(options);

/// <summary>A second store's context, for the hosts that configure two outboxes (#188).</summary>
public sealed class SecondMessagingDbContext(DbContextOptions<SecondMessagingDbContext> options)
    : MessagingTestDbContext(options);

/// <summary>A business row a test writes next to the messaging rows, in the same unit of work.</summary>
public sealed class BusinessRecord
{
    public Guid Id { get; set; }

    public string Value { get; set; } = string.Empty;
}

public abstract class MessagingTestDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<BusinessRecord> BusinessRecords => Set<BusinessRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyMessagingConfiguration();
        modelBuilder.Entity<BusinessRecord>().ToTable("BusinessRecords").HasKey(r => r.Id);
    }

    // SQLite has no native DateTimeOffset type; range comparisons (<, >, <=, >=) are untranslatable.
    // Storing as long (binary ticks via DateTimeOffsetToBinaryConverter) enables INTEGER comparisons.
    //
    // PostgreSQL is deliberately left on its native timestamptz mapping. The point of running the
    // same store against a real database is to exercise what a real deployment does, and forcing
    // the SQLite workaround onto Npgsql would quietly test a column type nobody ships.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        if (Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) != true)
        {
            return;
        }

        configurationBuilder
            .Properties<DateTimeOffset>()
            .HaveConversion<DateTimeOffsetToBinaryConverter>();
        configurationBuilder
            .Properties<DateTimeOffset?>()
            .HaveConversion<DateTimeOffsetToBinaryConverter>();
    }
}
