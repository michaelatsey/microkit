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
