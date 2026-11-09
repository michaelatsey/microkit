using System.Buffers.Text;
using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Identifiers;
using MicroKit.Persistence.Abstractions;
using MicroKit.Persistence.EntityFrameworkCore;
using MicroKit.Persistence.IntegrationTests.PostgreSql;
using MicroKit.Persistence.IntegrationTests.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// #197 phase 5: projections to DTOs from a specification, keyset (cursor) pagination with the root's key
/// appended, and the compiled finds, which evaluate a context-state filter per context. Every scenario runs on
/// PostgreSQL and on SQL Server.
/// </summary>
public abstract class ReadSideScenarios
{
    protected abstract void UseProvider(DbContextOptionsBuilder builder, string database);

    protected abstract IDatabaseFailureClassifier Classifier { get; }

    // ---- Projection --------------------------------------------------------------------------------

    [DockerRequiredFact]
    public async Task AProjection_AppliesTheSpecificationOrderingAndPagination_InTheDatabase_Untracked()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var opts = new QueryOptions<RsOrder>(new PriorityAtLeast(2))
            .WithIncludes(q => q.Include(o => o.Lines)) // dropped by a projection
            .WithOrdering(q => q.OrderByDescending(o => o.Priority).ThenBy(o => o.Number))
            .WithPagination(page: 1, pageSize: 3);

        var rows = await Reads(context).ListAsync(opts, o => new RsOrderRow(o.Number, o.Priority, o.Lines.Count));

        rows.ShouldBe([new RsOrderRow("O1", 3, 2), new RsOrderRow("O5", 3, 2), new RsOrderRow("O3", 2, 2)]);
        context.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [DockerRequiredFact]
    public async Task APagedProjection_CountsEveryMatch_AndProjectsThePage()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var opts = new QueryOptions<RsOrder>(new PriorityAtLeast(2)).WithOrdering(q => q.OrderBy(o => o.Number)).WithPagination(page: 2, pageSize: 2);

        var page = await Reads(context).ListPagedAsync(opts, o => o.Number);

        page.TotalCount.ShouldBe(5);
        page.Items.ShouldBe(["O4", "O5"]);
    }

    [DockerRequiredFact]
    public async Task AProjection_HidesSoftDeletedAggregates_UnlessWithDeleted()
    {
        await using var db = await CreateDatabaseAsync();
        await using (var context = db.Create())
        {
            var id = await context.Orders.Where(o => o.Number == "O1").Select(o => o.Id).SingleAsync();
            var orders = Orders(context);
            await orders.DeleteAsync((await orders.FindAsync(id)).ShouldNotBeNull());
            await new EfUnitOfWork<RsDbContext>(context).CommitAsync();
        }

        await using var reads = db.Create();
        (await Reads(reads).ListAsync(new QueryOptions<RsOrder>(), o => o.Number)).ShouldNotContain("O1");
        (await Reads(reads).ListAsync(new QueryOptions<RsOrder>().WithDeleted(), o => o.Number)).ShouldContain("O1");
    }

    [DockerRequiredFact]
    public async Task AProjectionOfTwoCollections_KeepsTheSplitHint()
    {
        await using var db = await CreateDatabaseAsync();
        var commands = new CommandLog();
        await using var context = db.Create(interceptor: commands);
        var opts = new QueryOptions<RsOrder>().WithOrdering(q => q.OrderBy(o => o.Number)).AsSplitQuery();

        var rows = await Reads(context).ListAsync(opts, o => new RsOrderCollections(
            o.Number, o.Lines.Select(l => l.Sku).ToList(), o.Payments.Select(p => p.Amount).ToList()));

        rows.Count.ShouldBe(RsSeed.Orders);
        rows.ShouldAllBe(r => r.Lines.Count == 2 && r.Payments.Count == 2);
        commands.Texts.Count.ShouldBe(3, "one query per collection: no cartesian product");
    }

    // ---- Keyset ------------------------------------------------------------------------------------

    [DockerRequiredFact]
    public async Task AKeysetProjectionOfTwoCollections_KeepsTheSplitHint()
    {
        await using var db = await CreateDatabaseAsync();
        var commands = new CommandLog();
        await using var context = db.Create(interceptor: commands);
        var opts = new QueryOptions<RsOrder>().AsSplitQuery().WithKeyset(KeysetPagination<RsOrder>.OrderBy(o => o.Number) with { PageSize = 3 });

        var page = await Reads(context).ListKeysetAsync(opts, o => new RsOrderCollections(
            o.Number, o.Lines.Select(l => l.Sku).ToList(), o.Payments.Select(p => p.Amount).ToList()));

        page.Items.Select(r => r.Number).ShouldBe(["O1", "O2", "O3"]);
        commands.Texts.Count.ShouldBe(3);
    }

    [DockerRequiredTheory]
    [InlineData(false, ">=")]
    [InlineData(true, "<=")]
    public async Task TheKeysetPredicate_BoundsTheFirstKey_SoAnIndexCanSeek(bool descending, string bound)
    {
        await using var db = await CreateDatabaseAsync();
        var commands = new CommandLog();
        await using var context = db.Create(interceptor: commands);
        var keyset = (descending ? KeysetPagination<RsOrder>.OrderByDescending(o => o.Priority) : KeysetPagination<RsOrder>.OrderBy(o => o.Priority))
            with { PageSize = 2 };
        var next = (await Reads(context).ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset))).Next;
        commands.Texts.Clear();

        await Reads(context).ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset with { After = next }));

        commands.Texts.ShouldHaveSingleItem().ShouldContain(bound, Case.Sensitive, "k1 >= v1 AND (k1 > v1 OR ...): a range an index seeks");
    }

    [DockerRequiredFact]
    public async Task ASortKeyReadThroughAnInterface_InGenericCode_IsAccepted()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var expected = await context.Orders.OrderByDescending(o => o.PlacedAt).ThenBy(o => o.Id).Select(o => o.Number).ToListAsync();

        // The shape a compiler emits for a member reached through an interface: ((IRsPlaced)x).PlacedAt.
        var x = System.Linq.Expressions.Expression.Parameter(typeof(RsOrder), "x");
        var throughInterface = System.Linq.Expressions.Expression.Lambda<Func<RsOrder, DateTimeOffset>>(
            System.Linq.Expressions.Expression.Property(System.Linq.Expressions.Expression.Convert(x, typeof(IRsPlaced)), nameof(IRsPlaced.PlacedAt)), x);

        var (walked, _) = await WalkAsync(Reads(context), new QueryOptions<RsOrder>().WithKeyset(KeysetPagination<RsOrder>.OrderByDescending(throughInterface) with { PageSize = 3 }), o => o.Number);
        var (generic, _) = await WalkAsync(Reads(context), new QueryOptions<RsOrder>().WithKeyset(RsKeysets.Newest<RsOrder>() with { PageSize = 3 }), o => o.Number);

        walked.ShouldBe(expected);
        generic.ShouldBe(expected);
    }

    [DockerRequiredFact]
    public async Task AnyAndCount_RefuseAKeyset_LikeTheLists()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var opts = new QueryOptions<RsOrder>().WithKeyset(KeysetPagination<RsOrder>.OrderBy(o => o.Number));

        (await Should.ThrowAsync<ArgumentException>(async () => await Reads(context).AnyAsync(opts))).Message.ShouldContain("ListKeysetAsync");
        (await Should.ThrowAsync<ArgumentException>(async () => await Reads(context).CountAsync(opts))).Message.ShouldContain("ListKeysetAsync");
    }

    [DockerRequiredTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(10)]
    public async Task WalkingKeysetPages_ReturnsEveryRowOnce_InTheDatabasesOrder_WithTheKeyBreakingTies(int pageSize)
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var expected = await context.Orders.OrderByDescending(o => o.Priority).ThenBy(o => o.PlacedAt).ThenBy(o => o.Id)
            .Select(o => o.Number).ToListAsync();
        var keyset = KeysetPagination<RsOrder>.OrderByDescending(o => o.Priority).ThenBy(o => o.PlacedAt) with { PageSize = pageSize };

        var (walked, pages) = await WalkAsync(Reads(context), new QueryOptions<RsOrder>().WithKeyset(keyset), o => o.Number);

        walked.ShouldBe(expected);
        walked.Count.ShouldBe(RsSeed.Orders);
        pages.ShouldBe((RsSeed.Orders + pageSize - 1) / pageSize, "the last page, full or not, has no next cursor");
    }

    [DockerRequiredFact]
    public async Task AKeysetOnAStronglyTypedId_WalksEveryRow_WithTheSpecificationApplied()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var expected = await context.Orders.Where(o => o.Priority >= 2).OrderBy(o => o.Id).Select(o => o.Number).ToListAsync();
        var opts = new QueryOptions<RsOrder>(new PriorityAtLeast(2)).WithKeyset(KeysetPagination<RsOrder>.OrderBy(o => o.Id) with { PageSize = 2 });

        var (walked, _) = await WalkAsync(Reads(context), opts, o => o.Number);

        walked.ShouldBe(expected);
    }

    [DockerRequiredFact]
    public async Task AKeysetOnACompositeKeyAndADateTimeOffset_WalksEveryRow()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var expected = await context.Shipments.OrderByDescending(s => s.At).ThenBy(s => s.Region).ThenBy(s => s.Number)
            .Select(s => s.Region + s.Number).ToListAsync();
        var reads = new EfReadRepository<RsShipment, RsDbContext>(context, new EfSpecificationEvaluator(), PaginationLimits.Default);
        var opts = new QueryOptions<RsShipment>().WithKeyset(KeysetPagination<RsShipment>.OrderByDescending(s => s.At) with { PageSize = 2 });

        var (walked, _) = await WalkAsync(reads, opts, s => s.Region + s.Number);

        walked.ShouldBe(expected);
        walked.Count.ShouldBe(RsSeed.Shipments);
    }

    [DockerRequiredFact]
    public async Task AKeysetPageOfAggregates_AppliesTheIncludes_Untracked()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var opts = new QueryOptions<RsOrder>().WithIncludes(q => q.Include(o => o.Lines))
            .WithKeyset(KeysetPagination<RsOrder>.OrderBy(o => o.Number) with { PageSize = 3 });

        var page = await Reads(context).ListKeysetAsync(opts);

        page.Items.Select(o => o.Number).ShouldBe(["O1", "O2", "O3"]);
        page.Items.ShouldAllBe(o => o.Lines.Count == 2);
        page.HasNextPage.ShouldBeTrue();
        context.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [DockerRequiredFact]
    public async Task ATamperedToken_IsRefused_ByTryParse_OrAtQueryTime()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var keyset = KeysetPagination<RsOrder>.OrderBy(o => o.Priority) with { PageSize = 2 };
        var token = (await Reads(context).ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset))).Next.ShouldNotBeNull().ToString();

        KeysetCursor.TryParse(token[..^2], out _).ShouldBeFalse("truncated");
        KeysetCursor.TryParse(token + "!", out _).ShouldBeFalse("not Base64Url");

        // Well formed, but the first value's tag says uint where the key is stored as int.
        var bytes = Base64Url.DecodeFromChars(token);
        bytes[10].ShouldBe((byte)6);
        bytes[10] = 7;
        KeysetCursor.TryParse(Base64Url.EncodeToString(bytes), out var forged).ShouldBeTrue();
        var thrown = await Should.ThrowAsync<ArgumentException>(async () =>
            await Reads(context).ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset with { After = forged })));
        thrown.Message.ShouldContain("does not match the sort key 'RsOrder.Priority'");
    }

    [DockerRequiredFact]
    public async Task AToken_ReusedWithAnotherOrdering_OrForAnotherAggregateType_IsRefused()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var byId = KeysetPagination<RsOrder>.OrderBy(o => o.Id) with { PageSize = 1 };
        var next = (await Reads(context).ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(byId))).Next.ShouldNotBeNull();

        (await Should.ThrowAsync<ArgumentException>(async () => await Reads(context).ListKeysetAsync(
            new QueryOptions<RsOrder>().WithKeyset(KeysetPagination<RsOrder>.OrderByDescending(o => o.Id) with { After = next }))))
            .Message.ShouldContain("another ordering");
        (await Should.ThrowAsync<ArgumentException>(async () => await Reads(context).ListKeysetAsync(
            new QueryOptions<RsOrder>().WithKeyset(KeysetPagination<RsOrder>.OrderBy(o => o.Priority) with { After = next }))))
            .Message.ShouldContain("another ordering");

        // Same key name (Id), same provider type (Guid), another aggregate.
        var invoices = new EfReadRepository<RsInvoice, RsDbContext>(context, new EfSpecificationEvaluator(), PaginationLimits.Default);
        (await Should.ThrowAsync<ArgumentException>(async () => await invoices.ListKeysetAsync(
            new QueryOptions<RsInvoice>().WithKeyset(KeysetPagination<RsInvoice>.OrderBy(i => i.Id) with { After = next }))))
            .Message.ShouldContain("another aggregate type");
    }

    public enum BadKey
    {
        NullableColumn,
        NotAProperty,
        UnsupportedProviderType,
        PrimitiveCollection,
    }

    [DockerRequiredTheory]
    [InlineData(BadKey.NullableColumn, "'RsOrder.Note' is nullable")]
    [InlineData(BadKey.NotAProperty, "is not a scalar property of 'RsOrder'")]
    [InlineData(BadKey.UnsupportedProviderType, "'RsOrder.Duration' is stored as 'TimeSpan'")]
    [InlineData(BadKey.PrimitiveCollection, "'RsOrder.Codes' is a primitive collection")]
    public async Task ASortKeyTheCursorCannotCarry_IsRefused_NamingIt(BadKey bad, string reason)
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var keyset = bad switch
        {
            BadKey.NullableColumn => KeysetPagination<RsOrder>.OrderBy(o => o.Note),
            BadKey.NotAProperty => KeysetPagination<RsOrder>.OrderBy(o => o.Number.Length),
            BadKey.UnsupportedProviderType => KeysetPagination<RsOrder>.OrderBy(o => o.Duration),
            _ => KeysetPagination<RsOrder>.OrderBy(o => o.Codes),
        };

        var thrown = await Should.ThrowAsync<Exception>(async () => await Reads(context).ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset)));

        thrown.ShouldBeAssignableTo(bad is BadKey.UnsupportedProviderType or BadKey.PrimitiveCollection
            ? typeof(InvalidOperationException)
            : typeof(ArgumentException));
        thrown.Message.ShouldContain(reason);
    }

    [DockerRequiredFact]
    public async Task AKeyset_OwnsTheOrderingAndThePage_AndIsBounded()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var keyset = KeysetPagination<RsOrder>.OrderBy(o => o.Number);
        var reads = new EfReadRepository<RsOrder, RsDbContext>(context, new EfSpecificationEvaluator(), new PaginationLimits(defaultPageSize: 2, maxPageSize: 3));

        (await reads.ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset))).Items.Count.ShouldBe(2, "the default page size");
        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () => await reads.ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset with { PageSize = 4 })));
        await Should.ThrowAsync<ArgumentException>(async () => await reads.ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset).WithOrdering(q => q.OrderBy(o => o.Number))));
        await Should.ThrowAsync<ArgumentException>(async () => await reads.ListKeysetAsync(new QueryOptions<RsOrder>().WithKeyset(keyset).WithPagination(1, 2)));
        await Should.ThrowAsync<ArgumentException>(async () => await reads.ListAsync(new QueryOptions<RsOrder>().WithKeyset(keyset)));
        await Should.ThrowAsync<ArgumentException>(async () => await reads.ListPagedAsync(new QueryOptions<RsOrder>().WithKeyset(keyset), o => o.Number));
    }

    // ---- Compiled finds ----------------------------------------------------------------------------

    [DockerRequiredFact]
    public async Task TheCompiledFinds_EvaluateAContextStateFilter_PerContext()
    {
        await using var db = await CreateDatabaseAsync();
        RsOrderId inA, inB;
        await using (var seed = db.Create())
        {
            inA = await seed.Orders.Where(o => o.Tenant == "a").Select(o => o.Id).FirstAsync();
            inB = await seed.Orders.Where(o => o.Tenant == "b").Select(o => o.Id).FirstAsync();
        }

        await using var tenantA = db.Create(tenant: "a");
        await using var tenantB = db.Create(tenant: "b");

        // The same compiled queries, two contexts, two tenants: each sees its own only.
        (await Reads(tenantA).FindAsync([inA])).ShouldNotBeNull();
        (await Reads(tenantA).FindAsync([inB])).ShouldBeNull();
        (await Reads(tenantB).FindAsync([inB])).ShouldNotBeNull();
        (await Reads(tenantB).FindAsync([inA])).ShouldBeNull();

        (await Orders(tenantA).FindAsync(inB)).ShouldBeNull();
        (await Orders(tenantB).FindAsync(inB)).ShouldNotBeNull().Lines.Count.ShouldBe(2, "the whole aggregate");
        (await Orders(tenantA).FindAsync(inA)).ShouldNotBeNull();
    }

    [DockerRequiredFact]
    public async Task TheCompiledFinds_ServeEachModel_OfOneContextType()
    {
        await using var db = await CreateDatabaseAsync();
        await using var first = db.Create();
        await using var other = db.Create(variant: 1);
        var id = await first.Orders.Select(o => o.Id).FirstAsync();

        (await Reads(first).FindAsync([id])).ShouldNotBeNull();
        (await Reads(other).FindAsync([id])).ShouldNotBeNull("another model gets its own compiled query");
        (await Orders(first).FindAsync(id)).ShouldNotBeNull();
        (await Orders(other).FindAsync(id)).ShouldNotBeNull();
    }

    // ---- Helpers ------------------------------------------------------------------------------------

    private static EfReadRepository<RsOrder, RsDbContext> Reads(RsDbContext context) =>
        new(context, new EfSpecificationEvaluator(), PaginationLimits.Default);

    private static EfRepository<RsOrder, RsOrderId, RsDbContext> Orders(RsDbContext context) => new(context);

    private static async Task<(List<TResult> Walked, int Pages)> WalkAsync<TAggregate, TResult>(
        IReadRepository<TAggregate> reads,
        QueryOptions<TAggregate> opts,
        System.Linq.Expressions.Expression<Func<TAggregate, TResult>> selector)
        where TAggregate : class, IAggregateRoot
    {
        var walked = new List<TResult>();
        var pages = 0;
        KeysetCursor? after = null;
        do
        {
            // Through the token, as a client would send it back.
            var page = await reads.ListKeysetAsync(opts.WithKeyset(opts.Keyset! with { After = after }), selector);
            walked.AddRange(page.Items);
            pages++;
            after = page.Next is { } next && KeysetCursor.TryParse(next.ToString(), out var parsed) ? parsed : null;
            if (pages > 50)
                throw new InvalidOperationException("the walk does not end");
        }
        while (after is not null);

        return (walked, pages);
    }

    private async Task<Database> CreateDatabaseAsync()
    {
        var db = new Database(this, "readside_" + Guid.NewGuid().ToString("N"));
        await using var context = db.Create();
        await context.Database.EnsureCreatedAsync();
        RsSeed.Into(context);
        await context.SaveChangesAsync();
        return db;
    }

    private sealed class Database(ReadSideScenarios owner, string name) : IAsyncDisposable
    {
        public RsDbContext Create(string? tenant = null, int variant = 0, CommandLog? interceptor = null)
        {
            var builder = new DbContextOptionsBuilder<RsDbContext>();
            owner.UseProvider(builder, name);
            if (interceptor is not null)
                builder.AddInterceptors(interceptor);
            ModelVariantCacheKeyFactory.Use(builder);
            builder.UseMicroKitPersistence(owner.Classifier);
            return new RsDbContext(builder.Options, variant) { Tenant = tenant };
        }

        public async ValueTask DisposeAsync()
        {
            await using var context = Create();
            await context.Database.EnsureDeletedAsync();
        }
    }

    /// <summary>Records the text of every command that reads.</summary>
    public sealed class CommandLog : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public List<string> Texts { get; } = [];

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Texts.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class PriorityAtLeast(int priority) : MicroKit.Domain.Specifications.Specification<RsOrder>
    {
        public override System.Linq.Expressions.Expression<Func<RsOrder, bool>> ToExpression() => o => o.Priority >= priority;
    }
}

[Collection(PostgreSqlSuite.Name)]
public sealed class ReadSidePostgreSqlTests(PostgreSqlSuiteFixture fixture) : ReadSideScenarios
{
    protected override IDatabaseFailureClassifier Classifier => MicroKit.Persistence.EntityFrameworkCore.PostgreSql.PostgreSqlFailureClassifier.Instance;

    protected override void UseProvider(DbContextOptionsBuilder builder, string database) =>
        builder.UseNpgsql(fixture.GetConnectionString(database));
}

[Collection(SqlServerSuite.Name)]
public sealed class ReadSideSqlServerTests(SqlServerSuiteFixture fixture) : ReadSideScenarios
{
    protected override IDatabaseFailureClassifier Classifier => MicroKit.Persistence.EntityFrameworkCore.SqlServer.SqlServerFailureClassifier.Instance;

    protected override void UseProvider(DbContextOptionsBuilder builder, string database) =>
        builder.UseSqlServer(fixture.GetConnectionString(database));
}

public readonly record struct RsOrderId(Guid Value) : IEntityId;

/// <summary>A capability an aggregate exposes through an interface, for generic read code.</summary>
public interface IRsPlaced
{
    DateTimeOffset PlacedAt { get; }
}

public static class RsKeysets
{
    // The compiler reads x.PlacedAt as ((IRsPlaced)x).PlacedAt: the key's target is a Convert of the parameter.
    public static KeysetPagination<T> Newest<T>()
        where T : class, IAggregateRoot, IRsPlaced =>
        KeysetPagination<T>.OrderByDescending(x => x.PlacedAt);
}

public sealed class RsOrder : IAggregateRoot, IRsPlaced
{
    public RsOrderId Id { get; init; } = new(Guid.NewGuid());

    public string Number { get; set; } = string.Empty;

    public int Priority { get; set; }

    public DateTimeOffset PlacedAt { get; set; }

    public string Tenant { get; set; } = "a";

    public string? Note { get; set; }

    public TimeSpan Duration { get; set; }

    public List<int> Codes { get; set; } = [];

    public List<RsLine> Lines { get; } = [];

    public List<RsPayment> Payments { get; } = [];
}

public sealed class RsPayment
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public decimal Amount { get; set; }
}

public sealed record RsOrderCollections(string Number, List<string> Lines, List<decimal> Payments);

public sealed class RsLine
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Sku { get; set; } = string.Empty;
}

/// <summary>Another aggregate whose key has the same name and provider type as <see cref="RsOrder"/>'s.</summary>
public sealed class RsInvoice : IAggregateRoot
{
    public RsOrderId Id { get; init; } = new(Guid.NewGuid());
}

/// <summary>An aggregate with a composite primary key.</summary>
public sealed class RsShipment : IAggregateRoot
{
    public string Region { get; init; } = string.Empty;

    public int Number { get; init; }

    public DateTimeOffset At { get; set; }
}

public sealed record RsOrderRow(string Number, int Priority, int Lines);

public static class RsSeed
{
    public const int Orders = 7;

    public const int Shipments = 5;

    private static readonly DateTimeOffset Start = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    // Ties on Priority and on PlacedAt: the appended id orders them.
    public static void Into(RsDbContext context)
    {
        int[] priorities = [3, 1, 2, 2, 3, 1, 2];
        for (var i = 0; i < Orders; i++)
        {
            var order = new RsOrder
            {
                Number = "O" + (i + 1),
                Priority = priorities[i],
                PlacedAt = Start.AddMinutes(i % 3),
                Tenant = i < 2 ? "b" : "a",
                Note = i % 2 == 0 ? null : "note",
            };
            order.Lines.Add(new RsLine { Sku = "A" });
            order.Lines.Add(new RsLine { Sku = "B" });
            order.Payments.Add(new RsPayment { Amount = 10m });
            order.Payments.Add(new RsPayment { Amount = 5m });
            context.Orders.Add(order);
        }

        context.Add(new RsInvoice());
        context.Add(new RsShipment { Region = "EU", Number = 2, At = Start });
        context.Add(new RsShipment { Region = "EU", Number = 1, At = Start });
        context.Add(new RsShipment { Region = "US", Number = 1, At = Start.AddHours(1) });
        context.Add(new RsShipment { Region = "AS", Number = 9, At = Start });
        context.Add(new RsShipment { Region = "US", Number = 3, At = Start.AddHours(-1) });
    }
}

public sealed class RsDbContext(DbContextOptions<RsDbContext> options, int variant = 0) : DbContext(options), IModelVariant
{
    public string? Tenant { get; set; }

    public DbSet<RsOrder> Orders => Set<RsOrder>();

    public DbSet<RsShipment> Shipments => Set<RsShipment>();

    object IModelVariant.Variant => variant;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RsOrder>(b =>
        {
            b.Property(o => o.Id).HasConversion(id => id.Value, v => new RsOrderId(v)).ValueGeneratedNever();
            b.Property(o => o.Number).HasMaxLength(variant == 1 ? 40 : 20);
            b.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
            b.HasMany(o => o.Payments).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
            b.HasSoftDelete();
            b.HasQueryFilter("Tenant", o => Tenant == null || o.Tenant == Tenant);
        });
        modelBuilder.Entity<RsLine>().Property(l => l.Id).ValueGeneratedNever();
        modelBuilder.Entity<RsPayment>(b =>
        {
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Amount).HasPrecision(18, 2);
        });
        modelBuilder.Entity<RsInvoice>().Property(i => i.Id).HasConversion(id => id.Value, v => new RsOrderId(v)).ValueGeneratedNever();
        modelBuilder.Entity<RsShipment>(b =>
        {
            b.HasKey(s => new { s.Region, s.Number });
            b.Property(s => s.Region).HasMaxLength(10);
        });
    }
}
