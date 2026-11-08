using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// <see cref="EfReadRepository{TAggregate,TContext}"/> list queries honour
/// <see cref="QueryOptions{TAggregate}.AsNoTrackingEnabled"/> whatever the context's default
/// <see cref="QueryTrackingBehavior"/>.
/// </summary>
public sealed class EfReadRepositoryTrackingTests
{
    [Fact]
    public async Task ListAsync_WithTracking_OnANoTrackingContext_ReturnsTrackedAggregates()
    {
        await using var connection = await OpenSeededAsync("stored");
        await using var context = CreateContext(connection, QueryTrackingBehavior.NoTracking);
        var sut = new EfReadRepository<ReadModelAggregate, ReadModelDbContext>(context, new EfSpecificationEvaluator(), PaginationLimits.Default);

        var found = (await sut.ListAsync(new QueryOptions<ReadModelAggregate>().WithTracking())).ShouldHaveSingleItem();

        context.Entry(found).State.ShouldBe(EntityState.Unchanged, "WithTracking() must override the context's NoTracking default");
        found.Name = "renamed";
        await context.SaveChangesAsync();
        await using var verify = CreateContext(connection, QueryTrackingBehavior.NoTracking);
        (await verify.Aggregates.SingleAsync()).Name.ShouldBe("renamed");
    }

    [Fact]
    public async Task ListAsync_ByDefault_OnATrackAllContext_ReturnsUntrackedAggregates()
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
