using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// #196 D14 (#172): an audit instant materialised by EF Core obeys the rules the Auditable*
/// constructors enforce — offset zero, never <c>default</c> — whatever the stored row holds.
/// SQLite stores a DateTimeOffset as text with its offset, like SQL Server's <c>datetimeoffset</c>.
/// </summary>
public sealed class AuditInstantNormalisationTests
{
    [Fact]
    public async Task AStoredInstantWithANonZeroOffset_IsMaterialisedAtOffsetZero()
    {
        await using var connection = await OpenAsync();
        var id = await InsertRawAsync(connection, createdAt: "2026-10-24 11:00:00+02:00");

        await using var context = CreateContext(connection);
        var ledger = await context.Ledgers.SingleAsync(l => l.Id == id);

        ledger.CreatedAt.Offset.ShouldBe(TimeSpan.Zero);
        ledger.CreatedAt.ShouldBe(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task AStoredDefaultInstant_IsRejectedOnMaterialisation()
    {
        await using var connection = await OpenAsync();
        var id = await InsertRawAsync(connection, createdAt: "0001-01-01 00:00:00+00:00");

        await using var context = CreateContext(connection);

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await context.Ledgers.SingleAsync(l => l.Id == id));
    }

    private static async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        return connection;
    }

    private static LedgerDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<LedgerDbContext>().UseSqlite(connection).Options);

    // Written behind EF Core's back, as a legacy row or another writer would.
    private static async Task<LedgerId> InsertRawAsync(SqliteConnection connection, string createdAt)
    {
        var id = new LedgerId(Guid.CreateVersion7());
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO ledgers (Id, Name, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy) " +
            "VALUES ($id, 'raw', $createdAt, 'user-1', NULL, NULL)";
        command.Parameters.AddWithValue("$id", id.Value.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$createdAt", createdAt);
        await command.ExecuteNonQueryAsync();
        return id;
    }
}

/// <summary>
/// #196 D14 on PostgreSQL, whose <c>timestamptz</c> only accepts offset zero: a range filter with a
/// non-UTC instant runs, because the parameter goes through the same normalisation.
/// </summary>
[Collection(MicroKit.Persistence.IntegrationTests.PostgreSql.PostgreSqlSuite.Name)]
public sealed class AuditInstantNormalisationPostgreSqlTests(MicroKit.Persistence.IntegrationTests.PostgreSql.PostgreSqlSuiteFixture fixture)
{
    [DockerRequiredFact]
    public async Task ARangeFilterWithANonUtcInstant_IsNormalisedAndMatches()
    {
        var connectionString = fixture.GetConnectionString("instants_" + Guid.NewGuid().ToString("N"));
        var options = new DbContextOptionsBuilder<LedgerDbContext>().UseNpgsql(connectionString).Options;
        var createdAt = new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

        await using (var context = new LedgerDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();
            context.Ledgers.Add(Ledger.Open(new LedgerId(Guid.CreateVersion7()), "open", createdAt, new MicroKit.Domain.ValueObjects.Common.Actor("user-1")));
            await context.SaveChangesAsync();
        }

        try
        {
            await using var read = new LedgerDbContext(options);
            var sameInstantInParis = createdAt.ToOffset(TimeSpan.FromHours(2));

            var found = await read.Ledgers.Where(l => l.CreatedAt >= sameInstantInParis).ToListAsync();

            found.ShouldHaveSingleItem().CreatedAt.ShouldBe(createdAt);

            // "No lower bound" — DateTimeOffset.MinValue is default(DateTimeOffset): rejecting a
            // default instant applies to stored rows, never to a query parameter.
            var unbounded = DateTimeOffset.MinValue;
            (await read.Ledgers.Where(l => l.CreatedAt >= unbounded).CountAsync()).ShouldBe(1);
        }
        finally
        {
            await using var cleanup = new LedgerDbContext(options);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }
}
