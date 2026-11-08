using MicroKit.Domain.Aggregates;
using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// <see cref="EfReadRepository{TAggregate,TContext}.FindAsync"/> shares the scoped
/// <see cref="DbContext"/> with the write side: it must never detach an entity the current
/// command already tracks, or the next commit silently writes nothing (#196 D2).
/// </summary>
public sealed class EfReadRepositoryFindTests
{
    [Fact]
    public async Task FindAsync_WhenAggregateIsTrackedAndModified_ReturnsItUntouched_AndTheCommitWritesIt()
    {
        await using var h = await Harness.CreateAsync();
        var id = await SeedAsync(h, "original");
        var tracked = await h.Context.Aggregates.SingleAsync(a => a.Id == id);
        tracked.Name = "renamed-by-the-command";

        var found = await h.Sut.FindAsync([id]);

        found.ShouldBeSameAs(tracked);
        h.Context.Entry(tracked).State.ShouldBe(EntityState.Modified,
            "a read through the read repository must not drop the command's pending change");

        await h.Context.SaveChangesAsync();
        (await ReadNameFromDatabaseAsync(h, id)).ShouldBe("renamed-by-the-command");
    }

    [Fact]
    public async Task FindAsync_WhenAggregateIsTrackedAndAdded_ReturnsItUntouched_AndTheCommitInsertsIt()
    {
        await using var h = await Harness.CreateAsync();
        var added = new ReadModelAggregate { Name = "staged" };
        h.Context.Add(added);

        var found = await h.Sut.FindAsync([added.Id]);

        found.ShouldBeSameAs(added);
        h.Context.Entry(added).State.ShouldBe(EntityState.Added);

        await h.Context.SaveChangesAsync();
        (await ReadNameFromDatabaseAsync(h, added.Id)).ShouldBe("staged");
    }

    [Fact]
    public async Task FindAsync_WhenAggregateIsTrackedAndDeleted_ReturnsItUntouched_AndTheCommitDeletesIt()
    {
        await using var h = await Harness.CreateAsync();
        var id = await SeedAsync(h, "to-delete");
        var tracked = await h.Context.Aggregates.SingleAsync(a => a.Id == id);
        h.Context.Remove(tracked);

        var found = await h.Sut.FindAsync([id]);

        found.ShouldBeSameAs(tracked);
        h.Context.Entry(tracked).State.ShouldBe(EntityState.Deleted,
            "a read must not drop the command's pending delete");

        await h.Context.SaveChangesAsync();
        (await ReadNameFromDatabaseAsync(h, id)).ShouldBeNull();
    }

    [Fact]
    public async Task FindAsync_WhenAggregateIsNotTracked_LoadsItDetached()
    {
        await using var h = await Harness.CreateAsync();
        var id = await SeedAsync(h, "stored");

        var found = await h.Sut.FindAsync([id]);

        found.ShouldNotBeNull();
        found.Name.ShouldBe("stored");
        h.Context.Entry(found).State.ShouldBe(EntityState.Detached,
            "an entity the read repository loaded itself is not left in the change tracker");
    }

    [Fact]
    public async Task FindAsync_WhenNotTracked_DoesNotWireTheLoadedAggregateIntoTrackedEntities()
    {
        await using var h = await Harness.CreateAsync();
        var id = await SeedAsync(h, "referenced");
        // A tracked entity of the current command references the aggregate by foreign key only.
        var note = new ReadModelNote { AggregateId = id };
        h.Context.Add(note);
        await h.Context.SaveChangesAsync();

        var found = await h.Sut.FindAsync([id]);

        found.ShouldNotBeNull();
        note.Aggregate.ShouldBeNull("the read must not fix up tracked navigations to an instance it then abandons");
        h.Context.Entry(note).State.ShouldBe(EntityState.Unchanged);
        await Should.NotThrowAsync(() => h.Context.SaveChangesAsync(),
            "a later commit must not try to insert the aggregate the read returned");
        h.Context.ChangeTracker.Entries<ReadModelAggregate>().ShouldBeEmpty();
    }

    [Fact]
    public async Task FindAsync_WhenNotTracked_QueriesTheKeyAsAParameter()
    {
        await using var h = await Harness.CreateAsync();
        var id = await SeedAsync(h, "stored");
        var commands = new List<string>();
        await using var logged = new ReadModelDbContext(new DbContextOptionsBuilder<ReadModelDbContext>()
            .UseSqlite(h.Connection)
            .LogTo(commands.Add, [Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuting])
            .Options);
        var sut = new EfReadRepository<ReadModelAggregate, ReadModelDbContext>(
            logged, new EfSpecificationEvaluator(), PaginationLimits.Default);

        (await sut.FindAsync([id])).ShouldNotBeNull();

        var sql = commands.ShouldHaveSingleItem();
        sql.ShouldNotContain(id.ToString(), Case.Insensitive, "the key is bound as a parameter, so one compiled query serves every id");
        sql.ShouldContain("@");
    }

    [Fact]
    public async Task FindAsync_WithTheWrongNumberOfKeyValues_Throws()
    {
        await using var h = await Harness.CreateAsync();

        await Should.ThrowAsync<ArgumentException>(async () => await h.Sut.FindAsync([Guid.NewGuid(), Guid.NewGuid()]));
    }

    [Fact]
    public async Task FindAsync_WithAKeyValueOfTheWrongType_ThrowsArgumentException()
    {
        await using var h = await Harness.CreateAsync();
        await SeedAsync(h, "stored");

        await Should.ThrowAsync<ArgumentException>(async () => await h.Sut.FindAsync([42]));
    }

    [Fact]
    public async Task FindAsync_OnAConvertedKey_WithTheProviderValueInsteadOfTheKeyType_ThrowsArgumentException()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PlainKeyDbContext>().UseSqlite(connection).Options;
        await using var context = new PlainKeyDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var sut = new EfReadRepository<PlainKeyAggregate, PlainKeyDbContext>(
            context, new EfSpecificationEvaluator(), PaginationLimits.Default);

        // The key's CLR type is PlainKey; its column holds a Guid. Passing the Guid is a caller error.
        await Should.ThrowAsync<ArgumentException>(async () => await sut.FindAsync([Guid.NewGuid()]));
    }

    [Fact]
    public async Task FindAsync_WithAStructKeyThatDefinesNoEqualityOperator_FindsTheAggregate()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PlainKeyDbContext>().UseSqlite(connection).Options;
        var key = new PlainKey(Guid.NewGuid());
        await using (var seed = new PlainKeyDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Aggregates.Add(new PlainKeyAggregate { Id = key, Name = "stored" });
            await seed.SaveChangesAsync();
        }

        await using var context = new PlainKeyDbContext(options);
        var sut = new EfReadRepository<PlainKeyAggregate, PlainKeyDbContext>(
            context, new EfSpecificationEvaluator(), PaginationLimits.Default);

        var found = await sut.FindAsync([key]);

        found.ShouldNotBeNull().Name.ShouldBe("stored");
    }

    [Fact]
    public async Task FindAsync_WhenMissing_ReturnsNull()
    {
        await using var h = await Harness.CreateAsync();
        (await h.Sut.FindAsync([Guid.NewGuid()])).ShouldBeNull();
    }

    [Fact]
    public async Task FindAsync_DoesNotRunAFullDetectChanges()
    {
        await using var h = await Harness.CreateAsync();
        var trackedId = await SeedAsync(h, "tracked");
        await h.Context.Aggregates.SingleAsync(a => a.Id == trackedId);
        var untrackedId = await SeedAsync(h, "untracked");
        h.Context.Entry(await h.Context.Aggregates.SingleAsync(a => a.Id == untrackedId)).State = EntityState.Detached;

        var fullDetections = 0;
        h.Context.ChangeTracker.DetectingAllChanges += (_, _) => fullDetections++;

        await h.Sut.FindAsync([trackedId]);
        await h.Sut.FindAsync([untrackedId]);

        fullDetections.ShouldBe(0, "a key lookup must not pay a DetectChanges over every tracked entity");
        h.Context.ChangeTracker.AutoDetectChangesEnabled.ShouldBeTrue("the caller's setting is restored");
    }

    private static async Task<Guid> SeedAsync(Harness h, string name)
    {
        var aggregate = new ReadModelAggregate { Name = name };
        h.Context.Add(aggregate);
        await h.Context.SaveChangesAsync();
        h.Context.Entry(aggregate).State = EntityState.Detached;
        return aggregate.Id;
    }

    private static async Task<string?> ReadNameFromDatabaseAsync(Harness h, Guid id)
    {
        await using var verify = new ReadModelDbContext(
            new DbContextOptionsBuilder<ReadModelDbContext>().UseSqlite(h.Connection).Options);
        return await verify.Aggregates.AsNoTracking().Where(a => a.Id == id).Select(a => a.Name).SingleOrDefaultAsync();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(SqliteConnection connection, ReadModelDbContext context)
        {
            Connection = connection;
            Context = context;
            Sut = new EfReadRepository<ReadModelAggregate, ReadModelDbContext>(context, new EfSpecificationEvaluator(), PaginationLimits.Default);
        }

        public SqliteConnection Connection { get; }

        public ReadModelDbContext Context { get; }

        public EfReadRepository<ReadModelAggregate, ReadModelDbContext> Sut { get; }

        // A SQLite in-memory database lives exactly as long as its connection.
        public static async Task<Harness> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var context = new ReadModelDbContext(
                new DbContextOptionsBuilder<ReadModelDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new Harness(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}

public sealed class ReadModelAggregate : IAggregateRoot
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
}

/// <summary>A tracked entity that references a <see cref="ReadModelAggregate"/> by foreign key.</summary>
public sealed class ReadModelNote
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid AggregateId { get; init; }

    public ReadModelAggregate? Aggregate { get; set; }
}

public sealed class ReadModelDbContext(DbContextOptions<ReadModelDbContext> options) : DbContext(options)
{
    public DbSet<ReadModelAggregate> Aggregates => Set<ReadModelAggregate>();

    public DbSet<ReadModelNote> Notes => Set<ReadModelNote>();
}

/// <summary>A strongly-typed key declared as a plain struct: no <c>==</c> operator, value equality only.</summary>
public readonly struct PlainKey(Guid value)
{
    public Guid Value { get; } = value;
}

public sealed class PlainKeyAggregate : IAggregateRoot
{
    public PlainKey Id { get; init; }

    public string Name { get; set; } = string.Empty;
}

public sealed class PlainKeyDbContext(DbContextOptions<PlainKeyDbContext> options) : DbContext(options)
{
    public DbSet<PlainKeyAggregate> Aggregates => Set<PlainKeyAggregate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<PlainKeyAggregate>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).HasConversion(k => k.Value, g => new PlainKey(g)).ValueGeneratedNever();
        });
}
