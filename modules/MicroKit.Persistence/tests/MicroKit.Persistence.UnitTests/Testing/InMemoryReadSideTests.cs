using MicroKit.Persistence.Testing;

namespace MicroKit.Persistence.UnitTests.Testing;

/// <summary>
/// #197 phase 5 in the in-memory doubles: projections and keyset pages follow the rules of the EF Core read side,
/// over the in-memory set.
/// </summary>
public sealed class InMemoryReadSideTests
{
    [Fact]
    public async Task AProjection_FollowsTheSpecificationTheOrderingAndThePagination()
    {
        var reads = Seeded();
        var opts = new QueryOptions<Ticket>(new OpenTickets()).WithOrderByDescending(t => t.Priority).WithThenBy(t => t.Title).WithPagination(1, 2);

        var titles = await reads.ListAsync(opts, t => t.Title.ToUpperInvariant());
        var page = await reads.ListPagedAsync(opts, t => new TicketRow(t.Title, t.Priority));

        titles.ShouldBe(["E", "B"]);
        page.Items.ShouldBe([new TicketRow("e", 3), new TicketRow("b", 2)]);
        page.TotalCount.ShouldBe(4);
    }

    [Fact]
    public async Task KeysetPages_ReturnEveryMatchingRowOnce_InOrder_WithTheKeyBreakingTies()
    {
        var reads = Seeded();
        var opts = new QueryOptions<Ticket>(new OpenTickets())
            .WithKeyset(KeysetPagination<Ticket>.OrderByDescending(t => t.Priority) with { PageSize = 1 });

        var walked = new List<string>();
        var pages = 0;
        KeysetCursor? after = null;
        do
        {
            var page = await reads.ListKeysetAsync(opts.WithKeyset(opts.Keyset! with { After = after }), t => t.Title);
            walked.AddRange(page.Items);
            after = page.Next;
            pages++;
        }
        while (after is not null);

        walked.Count.ShouldBe(4);
        walked[0].ShouldBe("e");
        walked.Skip(1).Take(2).OrderBy(t => t, StringComparer.Ordinal).ToArray().ShouldBe(["b", "c"], "a tie on Priority, broken by the id");
        walked[3].ShouldBe("a");
        pages.ShouldBe(4, "the last page has no next cursor");
    }

    [Fact]
    public async Task ACursorFromAnotherOrdering_OrAnotherDouble_IsRefused()
    {
        var reads = Seeded();
        var byPriority = new QueryOptions<Ticket>().WithKeyset(KeysetPagination<Ticket>.OrderBy(t => t.Priority) with { PageSize = 1 });
        var next = (await reads.ListKeysetAsync(byPriority)).Next.ShouldNotBeNull();

        var byTitle = new QueryOptions<Ticket>().WithKeyset(KeysetPagination<Ticket>.OrderBy(t => t.Title) with { After = next });
        (await Should.ThrowAsync<ArgumentException>(async () => await reads.ListKeysetAsync(byTitle))).Message.ShouldContain("another ordering");

        var elsewhere = byPriority.WithKeyset(byPriority.Keyset! with { After = next });
        (await Should.ThrowAsync<ArgumentException>(async () => await Seeded().ListKeysetAsync(elsewhere))).Message.ShouldContain("not issued by this");
    }

    [Fact]
    public async Task ANullableReferenceSortKey_OrAKeyThatIsNoProperty_IsRefused()
    {
        var reads = Seeded();

        (await Should.ThrowAsync<ArgumentException>(async () =>
            await reads.ListKeysetAsync(new QueryOptions<Ticket>().WithKeyset(KeysetPagination<Ticket>.OrderBy(t => t.Assignee)))))
            .Message.ShouldContain("Ticket.Assignee' is nullable");
        await Should.ThrowAsync<ArgumentException>(async () =>
            await reads.ListKeysetAsync(new QueryOptions<Ticket>().WithKeyset(KeysetPagination<Ticket>.OrderBy(t => t.Title.Length))));
    }

    [Fact]
    public async Task TheKeysetRules_AndThePageBound_Apply()
    {
        var reads = new InMemoryReadRepository<Ticket>(Tickets(), new PaginationLimits(defaultPageSize: 2, maxPageSize: 3));
        var keyset = KeysetPagination<Ticket>.OrderBy(t => t.Priority);

        (await reads.ListKeysetAsync(new QueryOptions<Ticket>().WithKeyset(keyset))).Items.Count.ShouldBe(2);
        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
            await reads.ListKeysetAsync(new QueryOptions<Ticket>().WithKeyset(keyset with { PageSize = 4 })));
        await Should.ThrowAsync<ArgumentException>(async () =>
            await reads.ListKeysetAsync(new QueryOptions<Ticket>().WithKeyset(keyset).WithPagination(1, 2)));
        await Should.ThrowAsync<ArgumentException>(async () =>
            await reads.ListAsync(new QueryOptions<Ticket>().WithKeyset(keyset)));
        await Should.ThrowAsync<ArgumentException>(async () =>
            await reads.ListPagedAsync(new QueryOptions<Ticket>().WithKeyset(keyset), t => t.Title));
    }

    [Fact]
    public async Task ASortKeyReadThroughAnInterface_InGenericCode_IsAccepted()
    {
        var x = System.Linq.Expressions.Expression.Parameter(typeof(Ticket), "x");
        var throughInterface = System.Linq.Expressions.Expression.Lambda<Func<Ticket, int>>(
            System.Linq.Expressions.Expression.Property(System.Linq.Expressions.Expression.Convert(x, typeof(IPrioritised)), nameof(IPrioritised.Priority)), x);

        (await Seeded().ListKeysetAsync(new QueryOptions<Ticket>().WithKeyset(Highest<Ticket>()), t => t.Title)).Items[0].ShouldBe("d");
        (await Seeded().ListKeysetAsync(new QueryOptions<Ticket>().WithKeyset(KeysetPagination<Ticket>.OrderByDescending(throughInterface)), t => t.Title))
            .Items[0].ShouldBe("d");
    }

    [Fact]
    public async Task AnyAndCount_RefuseAKeyset_LikeTheLists()
    {
        var opts = new QueryOptions<Ticket>().WithKeyset(KeysetPagination<Ticket>.OrderBy(t => t.Title));

        await Should.ThrowAsync<ArgumentException>(async () => await Seeded().AnyAsync(opts));
        await Should.ThrowAsync<ArgumentException>(async () => await Seeded().CountAsync(opts));
    }

    // The compiler reads x.Priority as ((IPrioritised)x).Priority.
    private static KeysetPagination<T> Highest<T>()
        where T : class, IAggregateRoot, IPrioritised =>
        KeysetPagination<T>.OrderByDescending(x => x.Priority);

    [Fact]
    public async Task TheRepositoryDouble_ReadsLikeTheReadDouble()
    {
        var repository = new InMemoryRepository<Ticket, TicketId>();
        foreach (var ticket in Tickets())
            await repository.AddAsync(ticket);

        (await repository.ListAsync(new QueryOptions<Ticket>(new OpenTickets()), t => t.Title)).Count.ShouldBe(4);
        (await repository.ListKeysetAsync(new QueryOptions<Ticket>().WithKeyset(KeysetPagination<Ticket>.OrderBy(t => t.Title))))
            .Items.Select(t => t.Title).ShouldBe(["a", "b", "c", "d", "e"]);
    }

    private static InMemoryReadRepository<Ticket> Seeded() => new(Tickets());

    private static Ticket[] Tickets() =>
    [
        new() { Id = new TicketId(Guid.NewGuid()), Title = "a", Priority = 1 },
        new() { Id = new TicketId(Guid.NewGuid()), Title = "b", Priority = 2 },
        new() { Id = new TicketId(Guid.NewGuid()), Title = "c", Priority = 2 },
        new() { Id = new TicketId(Guid.NewGuid()), Title = "d", Priority = 9, Closed = true },
        new() { Id = new TicketId(Guid.NewGuid()), Title = "e", Priority = 3 },
    ];

    public readonly record struct TicketId(Guid Value) : IEntityId;

    public interface IPrioritised
    {
        int Priority { get; }
    }

    public sealed class Ticket : IAggregateRoot, IPrioritised
    {
        public TicketId Id { get; init; }

        public string Title { get; init; } = "";

        public string? Assignee { get; init; }

        public int Priority { get; init; }

        public bool Closed { get; init; }
    }

    public sealed record TicketRow(string Title, int Priority);

    private sealed class OpenTickets : Specification<Ticket>
    {
        public override System.Linq.Expressions.Expression<Func<Ticket, bool>> ToExpression() => t => !t.Closed;
    }
}
