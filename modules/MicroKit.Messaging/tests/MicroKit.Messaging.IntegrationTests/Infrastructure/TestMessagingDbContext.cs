using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MicroKit.Messaging.IntegrationTests.Infrastructure;

public sealed class TestMessagingDbContext(DbContextOptions<TestMessagingDbContext> options)
    : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyMessagingConfiguration();

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
