using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// #196 D6: <see cref="EfReadRepository{TAggregate,TContext}"/> never loads a whole table into
/// one page, and rejects a page larger than the configured maximum.
/// </summary>
public sealed class EfReadRepositoryPaginationTests
{
    [Fact]
    public async Task ListPagedAsync_WithoutPagination_ReturnsTheBoundedDefaultPage()
    {
        await using var connection = await OpenSeededAsync(60);
        await using var context = CreateContext(connection);
        var sut = new EfReadRepository<ReadModelAggregate, ReadModelDbContext>(context, new EfSpecificationEvaluator(), PaginationLimits.Default);

        var result = await sut.ListPagedAsync(new QueryOptions<ReadModelAggregate>());

        result.TotalCount.ShouldBe(60);
        result.Page.ShouldBe(1);
        result.PageSize.ShouldBe(50);
        result.Items.Count.ShouldBe(50, "an unpaginated paged query never loads the whole table");
    }

    [Fact]
    public async Task ListPagedAsync_WhenThePageSizeExceedsTheMaximum_Throws()
    {
        await using var connection = await OpenSeededAsync(3);
        await using var context = CreateContext(connection);
        var sut = new EfReadRepository<ReadModelAggregate, ReadModelDbContext>(context, new EfSpecificationEvaluator(), PaginationLimits.Default);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
            await sut.ListPagedAsync(new QueryOptions<ReadModelAggregate>().WithPagination(page: 1, pageSize: 501)));
    }

    [Fact]
    public async Task ListAsync_WhenThePageSizeExceedsTheMaximum_Throws()
    {
        await using var connection = await OpenSeededAsync(3);
        await using var context = CreateContext(connection);
        var sut = new EfReadRepository<ReadModelAggregate, ReadModelDbContext>(context, new EfSpecificationEvaluator(), PaginationLimits.Default);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
            await sut.ListAsync(new QueryOptions<ReadModelAggregate>().WithPagination(page: 1, pageSize: 501)));
    }

    private static ReadModelDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ReadModelDbContext>().UseSqlite(connection).Options);

    private static async Task<SqliteConnection> OpenSeededAsync(int rows)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        context.Aggregates.AddRange(Enumerable.Range(0, rows).Select(i => new ReadModelAggregate { Name = $"row-{i:D3}" }));
        await context.SaveChangesAsync();
        return connection;
    }
}

/// <summary>#196 D6: the limits are configured once, at registration, and fail there when invalid.</summary>
public sealed class PaginationLimitsRegistrationTests
{
    [Fact]
    public void AddEntityFrameworkCore_RegistersTheDefaultLimits()
    {
        var services = new ServiceCollection();
        services.AddMicroKitPersistence(p => p.AddEntityFrameworkCore());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<PaginationLimits>()
            .ShouldBeSameAs(PaginationLimits.Default);
    }

    [Fact]
    public async Task ConfigurePagination_IsWhatAResolvedReadRepositoryEnforces()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddMicroKitPersistence(p => p.AddEntityFrameworkCore(ef => ef
            .AddDbContext<ReadModelDbContext>(o => o.UseSqlite(connection))
            .ConfigurePagination(new PaginationLimits(defaultPageSize: 2, maxPageSize: 4))));
        services.AddScoped<EfReadRepository<ReadModelAggregate, ReadModelDbContext>>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ReadModelDbContext>();
        await context.Database.EnsureCreatedAsync();
        context.Aggregates.AddRange(Enumerable.Range(0, 5).Select(i => new ReadModelAggregate { Name = $"r{i}" }));
        await context.SaveChangesAsync();
        var sut = scope.ServiceProvider.GetRequiredService<EfReadRepository<ReadModelAggregate, ReadModelDbContext>>();

        (await sut.ListPagedAsync(new QueryOptions<ReadModelAggregate>())).Items.Count.ShouldBe(2);
        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
            await sut.ListPagedAsync(new QueryOptions<ReadModelAggregate>().WithPagination(1, 5)));
    }

    [Fact]
    public void ConfigurePagination_WithAnInvalidConfiguration_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentOutOfRangeException>(() =>
            services.AddMicroKitPersistence(p => p.AddEntityFrameworkCore(ef =>
                ef.ConfigurePagination(new PaginationLimits(defaultPageSize: 100, maxPageSize: 10)))));
    }
}
