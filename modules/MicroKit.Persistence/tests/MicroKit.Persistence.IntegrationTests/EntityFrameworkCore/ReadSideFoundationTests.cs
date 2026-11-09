using System.Linq.Expressions;
using System.Reflection;
using MicroKit.Persistence.IntegrationTests.PostgreSql;
using MicroKit.Persistence.IntegrationTests.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit;

#pragma warning disable IL2060 // the spike builds generic methods at run time, as the read side will

namespace MicroKit.Persistence.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// #197 phase 5, step 0: the EF Core 10 facts the read side relies on, pinned without MicroKit, on PostgreSQL
/// and SQL Server. A keyset comparison built with a helper method translates for a value-converted id and for
/// types without a C# comparison operator; keys and a projection come back from one query; a compiled query
/// with includes, split and tracking works, refuses another model, and evaluates a context-state filter per
/// context.
/// </summary>
public abstract class ReadSideFoundationScenarios
{
    protected abstract void UseProvider(DbContextOptionsBuilder builder, string database);

    public enum Key
    {
        ConvertedId,
        RawGuid,
        Text,
        Instant,
        Flag,
    }

    [DockerRequiredTheory]
    [InlineData(Key.ConvertedId, false)]
    [InlineData(Key.ConvertedId, true)]
    [InlineData(Key.RawGuid, false)]
    [InlineData(Key.Text, true)]
    [InlineData(Key.Instant, false)]
    [InlineData(Key.Flag, true)]
    public async Task AKeysetComparisonBuiltWithAHelperMethod_PagesEveryRowOnceInTheDatabasesOrder(Key key, bool descending)
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var (property, type) = key switch
        {
            Key.ConvertedId => (nameof(FdItem.Id), typeof(FdItemId)),
            Key.RawGuid => (nameof(FdItem.Ref), typeof(Guid)),
            Key.Text => (nameof(FdItem.Name), typeof(string)),
            Key.Instant => (nameof(FdItem.At), typeof(DateTimeOffset)),
            _ => (nameof(FdItem.Flag), typeof(bool)),
        };

        var expected = await Ordered(context.Items, property, type, descending).ThenBy(i => i.Id).Select(i => i.Id).ToListAsync();
        var walked = new List<FdItemId>();
        FdItem? last = null;
        while (true)
        {
            var query = context.Items.AsNoTracking();
            if (last is not null)
                query = query.Where(After(property, type, descending, last));
            var page = await Ordered(query, property, type, descending).ThenBy(i => i.Id).Take(3).ToListAsync();
            if (page.Count == 0)
                break;
            walked.AddRange(page.Select(i => i.Id));
            last = page[^1];
        }

        walked.ShouldBe(expected);
        expected.Count.ShouldBe(FdSeed.Count);
    }

    [DockerRequiredFact]
    public async Task KeysAndAProjection_ComeBackFromOneQuery()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var item = Expression.Parameter(typeof(FdItem), "i");
        var keys = Expression.NewArrayInit(typeof(object),
            Expression.Convert(Expression.Property(item, nameof(FdItem.At)), typeof(object)),
            Expression.Convert(Expression.Property(item, nameof(FdItem.Id)), typeof(object)));
        Expression<Func<FdItem, FdDto>> selector = i => new FdDto(i.Name, i.Parts.Count);
        var body = Expression.New(
            typeof(FdRow).GetConstructors().Single(),
            keys,
            new ReplaceParameter(selector.Parameters[0], item).Visit(selector.Body));
        var projection = Expression.Lambda<Func<FdItem, FdRow>>(body, item);

        var query = context.Items.AsNoTracking().OrderBy(i => i.At).ThenBy(i => i.Id).Select(projection);
        var rows = await query.ToListAsync();

        rows.Count.ShouldBe(FdSeed.Count);
        rows[0].Keys[0].ShouldBeOfType<DateTimeOffset>();
        rows[0].Keys[1].ShouldBeOfType<FdItemId>("the model value, through the converter");
        rows.Sum(r => r.Item.Parts).ShouldBe(FdSeed.Count * 2);
        query.ToQueryString().ShouldNotBeNullOrEmpty();
    }

    [DockerRequiredFact]
    public async Task ACompiledQueryWithIncludesSplitAndTracking_LoadsTheGraph_AndRefusesAnotherModel()
    {
        await using var db = await CreateDatabaseAsync();
        var find = EF.CompileAsyncQuery((FdDbContext c, FdItemId id, CancellationToken ct) =>
            c.Items.AsTracking().Include(i => i.Parts).AsSplitQuery().FirstOrDefault(i => i.Id == id));

        await using (var context = db.Create())
        {
            var id = await context.Items.Select(i => i.Id).FirstAsync();
            var found = (await find(context, id, CancellationToken.None)).ShouldNotBeNull();
            found.Parts.Count.ShouldBe(2);
            context.Entry(found).State.ShouldBe(EntityState.Unchanged);
        }

        await using var other = db.Create(variant: 1);
        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => find(other, new FdItemId(Guid.NewGuid()), CancellationToken.None));
        thrown.Message.ShouldContain("different model");
    }

    [DockerRequiredFact]
    public async Task ACompiledQuery_EvaluatesAFilterReadingContextState_PerContext()
    {
        await using var db = await CreateDatabaseAsync();
        var count = EF.CompileAsyncQuery((FdDbContext c, CancellationToken ct) => c.Items.Count());

        await using var tenantA = db.Create();
        tenantA.Tenant = "a";
        await using var tenantB = db.Create();
        tenantB.Tenant = "b";

        var a = await count(tenantA, CancellationToken.None);
        var b = await count(tenantB, CancellationToken.None);
        tenantA.Tenant = "b";
        var aAsB = await count(tenantA, CancellationToken.None);

        a.ShouldBe(FdSeed.Count - FdSeed.InTenantB);
        b.ShouldBe(FdSeed.InTenantB);
        aAsB.ShouldBe(FdSeed.InTenantB, "the filter reads the context at each execution");
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static IOrderedQueryable<FdItem> Ordered(IQueryable<FdItem> query, string property, Type type, bool descending)
    {
        var item = Expression.Parameter(typeof(FdItem), "i");
        var key = Expression.Lambda(Expression.Property(item, property), item);
        var method = typeof(Queryable).GetMethods()
            .Single(m => m.Name == (descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy)) && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(FdItem), type);
        return (IOrderedQueryable<FdItem>)method.Invoke(null, [query, key])!;
    }

    // (k > v) OR (k = v AND id > lastId), with the value read from a holder, as the read side will.
    private static Expression<Func<FdItem, bool>> After(string property, Type type, bool descending, FdItem last)
    {
        var holder = new ValuesHolder([typeof(FdItem).GetProperty(property)!.GetValue(last), last.Id]);
        var values = Expression.Field(Expression.Constant(holder), nameof(ValuesHolder.Values));
        var item = Expression.Parameter(typeof(FdItem), "i");
        var key = Expression.Property(item, property);
        var value = Expression.Convert(Expression.ArrayIndex(values, Expression.Constant(0)), type);
        var id = Expression.Property(item, nameof(FdItem.Id));
        var lastId = Expression.Convert(Expression.ArrayIndex(values, Expression.Constant(1)), typeof(FdItemId));

        var beyond = descending ? Compare.Less(key, value) : Compare.Greater(key, value);
        var tie = Expression.AndAlso(Expression.Equal(key, value), Compare.Greater(id, lastId));
        return Expression.Lambda<Func<FdItem, bool>>(Expression.OrElse(beyond, tie), item);
    }

    private async Task<Database> CreateDatabaseAsync()
    {
        var db = new Database(this, "readfacts_" + Guid.NewGuid().ToString("N"));
        await using var context = db.Create();
        await context.Database.EnsureCreatedAsync();
        context.Items.AddRange(FdSeed.Items());
        await context.SaveChangesAsync();
        return db;
    }

    private sealed class Database(ReadSideFoundationScenarios owner, string name) : IAsyncDisposable
    {
        public FdDbContext Create(int variant = 0)
        {
            var builder = new DbContextOptionsBuilder<FdDbContext>();
            owner.UseProvider(builder, name);
            ModelVariantCacheKeyFactory.Use(builder);
            return new FdDbContext(builder.Options, variant);
        }

        public async ValueTask DisposeAsync()
        {
            await using var context = Create();
            await context.Database.EnsureDeletedAsync();
        }
    }

    private sealed class ValuesHolder(object?[] values)
    {
        public readonly object?[] Values = values;
    }

    private sealed class ReplaceParameter(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }

    /// <summary>
    /// Comparisons for types without a C# operator (a record-struct id, <c>bool</c>, <c>string</c>, <c>Guid</c> where
    /// it has none): a binary node whose method EF Core is expected to ignore, translating the operator itself.
    /// </summary>
    private static class Compare
    {
        private static readonly MethodInfo GreaterThanMethod = typeof(Compare).GetMethod(nameof(GreaterThan), BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly MethodInfo LessThanMethod = typeof(Compare).GetMethod(nameof(LessThan), BindingFlags.NonPublic | BindingFlags.Static)!;

        public static BinaryExpression Greater(Expression left, Expression right) =>
            Expression.GreaterThan(left, right, liftToNull: false, GreaterThanMethod.MakeGenericMethod(left.Type));

        public static BinaryExpression Less(Expression left, Expression right) =>
            Expression.LessThan(left, right, liftToNull: false, LessThanMethod.MakeGenericMethod(left.Type));

        private static bool GreaterThan<T>(T left, T right) => Comparer<T>.Default.Compare(left, right) > 0;

        private static bool LessThan<T>(T left, T right) => Comparer<T>.Default.Compare(left, right) < 0;
    }
}

[Collection(PostgreSqlSuite.Name)]
public sealed class ReadSideFoundationPostgreSqlTests(PostgreSqlFixture fixture) : ReadSideFoundationScenarios
{
    protected override void UseProvider(DbContextOptionsBuilder builder, string database) =>
        builder.UseNpgsql(new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = database }.ConnectionString);
}

[Collection(SqlServerSuite.Name)]
public sealed class ReadSideFoundationSqlServerTests(SqlServerFixture fixture) : ReadSideFoundationScenarios
{
    protected override void UseProvider(DbContextOptionsBuilder builder, string database) =>
        builder.UseSqlServer(new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = database }.ConnectionString);
}

public readonly record struct FdItemId(Guid Value);

public sealed class FdItem
{
    public FdItemId Id { get; init; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }

    public bool Flag { get; set; }

    public Guid Ref { get; set; }

    public string Tenant { get; set; } = "a";

    public List<FdPart> Parts { get; } = [];
}

public sealed class FdPart
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

public sealed record FdDto(string Name, int Parts);

public sealed record FdRow(object?[] Keys, FdDto Item);

public static class FdSeed
{
    public const int Count = 10;

    public const int InTenantB = 3;

    // Ties on every key but the id: the appended id decides.
    public static IEnumerable<FdItem> Items()
    {
        var at = new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < Count; i++)
        {
            var item = new FdItem
            {
                Id = new FdItemId(Guid.NewGuid()),
                Name = i % 3 == 0 ? "same" : "n" + i,
                At = at.AddMinutes(i % 4),
                Flag = i % 2 == 0,
                Ref = Guid.NewGuid(),
                Tenant = i < InTenantB ? "b" : "a",
            };
            item.Parts.Add(new FdPart());
            item.Parts.Add(new FdPart());
            yield return item;
        }
    }
}

public sealed class FdDbContext(DbContextOptions<FdDbContext> options, int variant = 0) : DbContext(options), IModelVariant
{
    public string? Tenant { get; set; }

    public DbSet<FdItem> Items => Set<FdItem>();

    object IModelVariant.Variant => variant;

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<FdItem>(b =>
        {
            b.Property(i => i.Id).HasConversion(id => id.Value, v => new FdItemId(v)).ValueGeneratedNever();
            b.Property(i => i.Name).HasMaxLength(50);
            b.HasMany(i => i.Parts).WithOne().HasForeignKey("ItemId");
            b.HasQueryFilter("Tenant", i => Tenant == null || i.Tenant == Tenant);
            if (variant == 1)
                b.Property(i => i.Name).HasMaxLength(60);
        });
}
