using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MicroKit.Persistence.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// ADR-005. The tracker-state cases build the model without ever opening the connection;
/// the two cases that assert what reached the database open a SQLite in-memory connection and
/// keep it alive for the duration of the test.
/// The model types (<c>TestDbContext</c>, <c>TestAggregate</c>) are shared with
/// <c>EfDomainEventsProviderTests</c>.
/// </summary>
public sealed class EfUnitOfWorkDiscardChangesTests
{
    [Fact]
    public void DiscardChanges_AfterStagingEntities_LeavesNothingPending()
    {
        using var context = CreateContext();
        context.Add(new TestAggregate());
        context.Add(new TestAggregate());
        var sut = new EfUnitOfWork<TestDbContext>(context);
        context.ChangeTracker.Entries().Count().ShouldBe(2, "the entities are staged before the discard");

        sut.DiscardChanges();

        context.ChangeTracker.Entries().ShouldBeEmpty();
        context.ChangeTracker.HasChanges().ShouldBeFalse();
    }

    [Fact]
    public void DiscardChanges_OnEmptyChangeTracker_DoesNotThrow()
    {
        using var context = CreateContext();
        var sut = new EfUnitOfWork<TestDbContext>(context);

        Should.NotThrow(() => sut.DiscardChanges());

        context.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public void DiscardChanges_CalledTwice_DiscardsEachTime()
    {
        using var context = CreateContext();
        context.Add(new TestAggregate());
        var sut = new EfUnitOfWork<TestDbContext>(context);

        sut.DiscardChanges();
        context.ChangeTracker.Entries().ShouldBeEmpty();

        // A second command in the same scope stages more work before the second discard,
        // so the call has something to abandon rather than re-asserting the first call's result.
        context.Add(new TestAggregate());
        context.ChangeTracker.Entries().Count().ShouldBe(1);

        Should.NotThrow(() => sut.DiscardChanges());

        context.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public void DiscardChanges_DetachesStagedEntity()
    {
        using var context = CreateContext();
        var aggregate = new TestAggregate();
        context.Add(aggregate);
        var sut = new EfUnitOfWork<TestDbContext>(context);
        context.Entry(aggregate).State.ShouldBe(EntityState.Added);

        sut.DiscardChanges();

        // The documented ADR-005 consequence: a reference held across the discard goes detached.
        context.Entry(aggregate).State.ShouldBe(EntityState.Detached);
    }

    [Fact]
    public async Task DiscardChanges_ThenCommitAsync_WritesNothing()
    {
        await using var connection = await OpenConnectionAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var sut = new EfUnitOfWork<TestDbContext>(context);
        context.Add(new TestAggregate());

        sut.DiscardChanges();
        await sut.CommitAsync();

        (await context.Aggregates.CountAsync())
            .ShouldBe(0, "a discarded change set must never reach the database");
    }

    [Fact]
    public async Task DiscardChanges_AfterCommit_LeavesCommittedRowsIntact()
    {
        await using var connection = await OpenConnectionAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var sut = new EfUnitOfWork<TestDbContext>(context);
        context.Add(new TestAggregate());
        await sut.CommitAsync();

        sut.DiscardChanges();

        (await context.Aggregates.CountAsync())
            .ShouldBe(1, "the discard is in-memory only — it is not a rollback");
    }

    [Fact]
    public async Task DiscardChanges_WithModifiedAndDeletedEntries_WritesNeitherChange()
    {
        await using var connection = await OpenConnectionAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var sut = new EfUnitOfWork<TestDbContext>(context);
        context.AddRange(new TestAggregate { Name = "original" }, new TestAggregate { Name = "keep-me" });
        await sut.CommitAsync();
        context.ChangeTracker.Clear();

        var toModify = await context.Aggregates.SingleAsync(a => a.Name == "original");
        toModify.Name = "touched";
        var toDelete = await context.Aggregates.SingleAsync(a => a.Name == "keep-me");
        context.Remove(toDelete);
        context.ChangeTracker.Entries<TestAggregate>().Select(e => e.State)
            .ShouldBe([EntityState.Modified, EntityState.Deleted], ignoreOrder: true);

        sut.DiscardChanges();
        await sut.CommitAsync();

        context.ChangeTracker.Clear();
        var names = await context.Aggregates.Select(a => a.Name).ToListAsync();
        names.ShouldBe(["original", "keep-me"], ignoreOrder: true,
            "neither the UPDATE nor the DELETE may survive the discard");
    }

    [Fact]
    public async Task ExecuteAsync_DiscardInsideTransaction_KeepsTheFlushAndDropsTheStagedSet()
    {
        await using var connection = await OpenConnectionAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var sut = new EfUnitOfWork<TestDbContext>(context);

        // The production call path: the discard runs inside ExecuteAsync, transaction open.
        await sut.ExecuteAsync(
            static async ((EfUnitOfWork<TestDbContext> Uow, TestDbContext Ctx) s, CancellationToken ct) =>
            {
                s.Ctx.Add(new TestAggregate { Name = "flushed-in-tx" });
                await s.Uow.CommitAsync(ct);

                s.Ctx.Add(new TestAggregate { Name = "staged-then-discarded" });
                s.Uow.DiscardChanges();
            },
            (sut, context));

        // The next command in the same scope flushes its own work. This is what makes the
        // assertion real: without the discard, the residue rides along with this commit.
        context.Add(new TestAggregate { Name = "next-command" });
        await sut.CommitAsync();

        context.ChangeTracker.Clear();
        var names = await context.Aggregates.Select(a => a.Name).ToListAsync();
        names.ShouldBe(["flushed-in-tx", "next-command"], ignoreOrder: true,
            "the transaction committed what was already flushed; the discarded set reached nothing");
    }

    [Fact]
    public async Task ExecuteAsync_DiscardThenThrow_RollsBackTheFlushAndKeepsEarlierRows()
    {
        await using var connection = await OpenConnectionAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var sut = new EfUnitOfWork<TestDbContext>(context);

        // Committed before the transaction begins — must survive the rollback.
        context.Add(new TestAggregate { Name = "committed-earlier" });
        await sut.CommitAsync();
        context.ChangeTracker.Clear();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await sut.ExecuteAsync(
                static async ((EfUnitOfWork<TestDbContext> Uow, TestDbContext Ctx) s, CancellationToken ct) =>
                {
                    s.Ctx.Add(new TestAggregate { Name = "flushed-in-doomed-tx" });
                    await s.Uow.CommitAsync(ct);

                    s.Ctx.Add(new TestAggregate { Name = "staged-then-discarded" });
                    s.Uow.DiscardChanges();
                    throw new InvalidOperationException("handler failed");
                },
                (sut, context)));

        thrown.Message.ShouldBe("handler failed");

        // EF Core's rollback does not reset the change tracker, so this later commit is exactly
        // what would have written the failed command's residue.
        context.Add(new TestAggregate { Name = "next-command" });
        await sut.CommitAsync();

        context.ChangeTracker.Clear();
        var names = await context.Aggregates.Select(a => a.Name).ToListAsync();
        names.ShouldBe(["committed-earlier", "next-command"], ignoreOrder: true,
            "the rollback undid the in-transaction flush; the discard kept the staged set out of the next commit");
    }

    // The connection is never opened: the model is built, nothing is executed.
    private const string InMemoryConnectionString = "DataSource=:memory:";

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(InMemoryConnectionString)
            .Options;

        return new TestDbContext(options);
    }

    // A SQLite in-memory database lives exactly as long as its connection — the caller keeps it open.
    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(InMemoryConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static TestDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .Options;

        return new TestDbContext(options);
    }
}
