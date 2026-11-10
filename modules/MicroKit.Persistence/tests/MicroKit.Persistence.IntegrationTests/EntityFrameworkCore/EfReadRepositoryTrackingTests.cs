using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// <see cref="EfReadRepository{TAggregate,TContext}"/> never tracks, whatever the context's default
/// <see cref="QueryTrackingBehavior"/> (#197 phase 1: <c>WithTracking()</c> is gone).
/// </summary>
public sealed class EfReadRepositoryTrackingTests
{
    [Fact]
    public async Task ListAsync_OnATrackAllContext_ReturnsUntrackedAggregates()
    {
        await using var connection = await OpenSeededAsync("stored");
        await using var context = CreateContext(connection, QueryTrackingBehavior.TrackAll);
        var sut = new EfReadRepository<ReadModelAggregate, ReadModelDbContext>(context, new EfSpecificationEvaluator(), PaginationLimits.Default);

        var found = (await sut.ListAsync(new QueryOptions<ReadModelAggregate>())).ShouldHaveSingleItem();

        context.Entry(found).State.ShouldBe(EntityState.Detached);
    }

    private static ReadModelDbContext CreateContext(SqliteConnection connection, QueryTrackingBehavior tracking) =>
        new(new DbContextOptionsBuilder<ReadModelDbContext>()
            .UseSqlite(connection)
            .UseQueryTrackingBehavior(tracking)
            .Options);

    private static async Task<SqliteConnection> OpenSeededAsync(string name)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection, QueryTrackingBehavior.TrackAll);
        await context.Database.EnsureCreatedAsync();
        context.Add(new ReadModelAggregate { Name = name });
        await context.SaveChangesAsync();
        return connection;
    }
}
