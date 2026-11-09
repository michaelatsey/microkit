using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Identifiers;
using MicroKit.Persistence.Abstractions;
using MicroKit.Persistence.EntityFrameworkCore;
using MicroKit.Persistence.IntegrationTests.PostgreSql;
using MicroKit.Persistence.IntegrationTests.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// #197 phase 2: every aggregate root carries an application-managed version, bumped by every save
/// that writes any entity of its aggregate. Every scenario runs on PostgreSQL and on SQL Server.
/// </summary>
public abstract class AggregateVersionScenarios
{
    /// <summary>Configures the provider on <paramref name="database"/>, with a retrying strategy when asked.</summary>
    protected abstract void UseProvider(DbContextOptionsBuilder builder, string database, int? maxRetryCount = null);

    // ---- The model ---------------------------------------------------------------------------

    [DockerRequiredFact]
    public void TheVersion_IsAShadowLongConcurrencyToken_OnTheTopMostRootOnly()
    {
        using var carts = new CartDbContext(Options<CartDbContext>("unused"));
        var token = carts.Model.FindEntityType(typeof(Cart))!.FindProperty(AggregateVersion.PropertyName).ShouldNotBeNull();
        token.IsShadowProperty().ShouldBeTrue();
        token.ClrType.ShouldBe(typeof(long));
        token.IsConcurrencyToken.ShouldBeTrue();
        carts.Model.FindEntityType(typeof(CartLine))!.FindProperty(AggregateVersion.PropertyName).ShouldBeNull("a child has none");
        carts.Model.FindEntityType(typeof(AuditRow))!.FindProperty(AggregateVersion.PropertyName).ShouldBeNull("not an aggregate");

        using var pets = new PetDbContext(Options<PetDbContext>("unused"));
        pets.Model.FindEntityType(typeof(Pet))!.FindDeclaredProperty(AggregateVersion.PropertyName).ShouldNotBeNull();
        pets.Model.FindEntityType(typeof(Dog))!.FindDeclaredProperty(AggregateVersion.PropertyName).ShouldBeNull("a derived root shares its base's");
    }

    [DockerRequiredFact]
    public void TheVersionColumn_FollowsTheConsumersNamingConvention()
    {
        using var context = new SnakeCaseCartDbContext(Options<SnakeCaseCartDbContext>("unused"));
        var cart = context.Model.FindEntityType(typeof(Cart))!;

        cart.FindProperty(AggregateVersion.PropertyName)!
            .GetColumnName(StoreObjectIdentifier.Table(cart.GetTableName()!, cart.GetSchema()))
            .ShouldBe("aggregate_version");
    }

    [DockerRequiredTheory]
    [InlineData(VersionMisuse.ClrMember, "maps it to a CLR member")]
    [InlineData(VersionMisuse.NotLong, "not 'long'")]
    [InlineData(VersionMisuse.NotAToken, "not to be a concurrency token")]
    [InlineData(VersionMisuse.SameColumn, "is also mapped by")]
    public void AMisusedVersion_FailsModelBuilding_NamingTheRoot(VersionMisuse misuse, string reason)
    {
        using var context = new MisusedVersionDbContext(Options<MisusedVersionDbContext>("unused"), misuse);

        var thrown = Should.Throw<InvalidOperationException>(() => _ = context.Model);

        thrown.Message.ShouldContain("'AggregateVersion' is reserved on the aggregate root");
        thrown.Message.ShouldContain(reason);
    }

    [DockerRequiredFact]
    public void AnAggregateVersionPropertyInAnotherTable_IsNotACollision()
    {
        using var context = new MisusedVersionDbContext(Options<MisusedVersionDbContext>("unused"), VersionMisuse.OtherTable);

        Should.NotThrow(() => _ = context.Model);
    }

    [DockerRequiredFact]
    public async Task AColumnRenamedByTheApplication_HoldsTheVersion()
    {
        await using var db = await CreateDatabaseAsync<RenamedVersionDbContext>(o => new RenamedVersionDbContext(o));
        var id = new CartId(Guid.NewGuid());
        await using (var context = db.Create())
        {
            await new EfRepository<Cart, CartId, RenamedVersionDbContext>(context).AddAsync(new Cart { Id = id, Owner = "a" });
            await Commit(context);
        }

        await using (var context = db.Create())
        {
            var cart = (await new EfRepository<Cart, CartId, RenamedVersionDbContext>(context).FindAsync(id)).ShouldNotBeNull();
            cart.Owner = "b";
            await Commit(context);
            var type = context.Model.FindEntityType(typeof(Cart))!;
            type.FindProperty(AggregateVersion.PropertyName)!
                .GetColumnName(StoreObjectIdentifier.Table(type.GetTableName()!, type.GetSchema())).ShouldBe("row_version");
        }

        await using var verify = db.Create();
        (await verify.Set<Cart>().Where(c => c.Id == id).Select(c => EF.Property<long>(c, AggregateVersion.PropertyName)).SingleAsync())
            .ShouldBe(2);
    }

    // ---- What bumps --------------------------------------------------------------------------

    [DockerRequiredFact]
    public async Task AnInsertedAggregate_StartsAtOne_AndAnAddedRootReadsZeroUntilItsCommit()
    {
        await using var db = await CreateCartDatabaseAsync();
        await using var context = db.Create();
        var carts = Carts(context);
        var cart = new Cart { Id = new CartId(Guid.NewGuid()), Owner = "new" };
        await carts.AddAsync(cart);

        carts.GetVersion(cart).ShouldBe(0);
        await Commit(context);

        carts.GetVersion(cart).ShouldBe(1);
        (await StoredVersionAsync(db, cart.Id)).ShouldBe(1);
    }

    public enum CartChange
    {
        RootProperty,
        ChildProperty,
        GrandChildProperty,
        OwnedProperty,
        ChildAdded,
        ChildDeleted,
        ManyToManyLinkAdded,
        PrincipalSideChildProperty,
        PrincipalSideChildReplaced,
    }

    [DockerRequiredTheory]
    [InlineData(CartChange.RootProperty)]
    [InlineData(CartChange.ChildProperty)]
    [InlineData(CartChange.GrandChildProperty)]
    [InlineData(CartChange.OwnedProperty)]
    [InlineData(CartChange.ChildAdded)]
    [InlineData(CartChange.ChildDeleted)]
    [InlineData(CartChange.ManyToManyLinkAdded)]
    [InlineData(CartChange.PrincipalSideChildProperty)]
    [InlineData(CartChange.PrincipalSideChildReplaced)]
    public async Task AnyWriteInsideTheBoundary_BumpsTheRootOnce(CartChange change)
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);

        await using (var context = db.Create())
        {
            var cart = (await Carts(context).FindAsync(id)).ShouldNotBeNull();
            Apply(context, cart, change);
            await Commit(context);
        }

        (await StoredVersionAsync(db, id)).ShouldBe(2);
    }

    [DockerRequiredFact]
    public async Task AGrandChildUnderAChildWithAByteArrayKey_BumpsTheRoot()
    {
        await using var db = await CreateDatabaseAsync<ArchiveDbContext>(o => new ArchiveDbContext(o));
        var archive = new Archive { Id = new CartId(Guid.NewGuid()) };
        var document = new ArchivedDocument { Code = [1, 2, 3], Title = "minutes" };
        document.Pages.Add(new ArchivedPage { Text = "page one" });
        archive.Documents.Add(document);
        await using (var context = db.Create())
        {
            await new EfRepository<Archive, CartId, ArchiveDbContext>(context).AddAsync(archive);
            await new EfUnitOfWork<ArchiveDbContext>(context).CommitAsync();
        }

        await using (var context = db.Create())
        {
            var loaded = (await new EfRepository<Archive, CartId, ArchiveDbContext>(context).FindAsync(archive.Id)).ShouldNotBeNull();
            // The page's foreign key is another byte[] instance than its document's key: matched by
            // EF Core's key comparer, not by reference.
            loaded.Documents.Single().Pages.Single().Text = "page one, amended";
            await new EfUnitOfWork<ArchiveDbContext>(context).CommitAsync();
        }

        await using var verify = db.Create();
        (await verify.Set<Archive>().Where(a => a.Id == archive.Id).Select(a => EF.Property<long>(a, AggregateVersion.PropertyName)).SingleAsync())
            .ShouldBe(2);
    }

    [DockerRequiredFact]
    public async Task TwoSaves_BumpTwice()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);
        await using var context = db.Create();
        var carts = Carts(context);
        var cart = (await carts.FindAsync(id)).ShouldNotBeNull();

        cart.Owner = "first";
        await Commit(context);
        cart.Lines[0].Sku = "second";
        await Commit(context);

        carts.GetVersion(cart).ShouldBe(3);
        (await StoredVersionAsync(db, id)).ShouldBe(3);
    }

    [DockerRequiredFact]
    public async Task ASaveThatFailsAndIsRepeated_BumpsOnce()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);
        var failOnce = new FailingInsertInterceptor(failures: 1, failure: () => new InvalidOperationException("the insert failed"));
        await using var context = db.Create(o => o.AddInterceptors(failOnce));
        var cart = (await Carts(context).FindAsync(id)).ShouldNotBeNull();
        cart.Lines.Add(new CartLine { Sku = "added" });

        await Should.ThrowAsync<PersistenceException>(() => Commit(context));
        Carts(context).GetVersion(cart).ShouldBe(1, "the failed save wrote nothing: its bump is undone");
        await Commit(context);

        failOnce.Thrown.ShouldBe(1);
        (await StoredVersionAsync(db, id)).ShouldBe(2, "computed from the original value: the repeated save does not add twice");
    }

    // ---- Moves, and roots that are not tracked -------------------------------------------------

    [DockerRequiredFact]
    public async Task AChildMovedBetweenTwoRecordedAggregates_BumpsBoth()
    {
        await using var db = await CreateCartDatabaseAsync();
        var from = await SeedCartAsync(db);
        var to = await SeedCartAsync(db);

        await using (var context = db.Create())
        {
            var carts = Carts(context);
            var source = (await carts.FindAsync(from)).ShouldNotBeNull();
            var target = (await carts.FindAsync(to)).ShouldNotBeNull();
            var line = source.Lines[0];
            source.Lines.Remove(line);
            target.Lines.Add(line);
            await Commit(context);
        }

        (await StoredVersionAsync(db, from)).ShouldBe(2);
        (await StoredVersionAsync(db, to)).ShouldBe(2);
    }

    [DockerRequiredFact]
    public async Task AChildMovedToAnAggregateNotLoadedByItsRepository_FailsTheSave_NamingTheRoots()
    {
        await using var db = await CreateCartDatabaseAsync();
        var from = await SeedCartAsync(db);
        var to = await SeedCartAsync(db);

        await using (var context = db.Create())
        {
            var source = (await Carts(context).FindAsync(from)).ShouldNotBeNull();
            var target = await context.Carts.Include(c => c.Lines).SingleAsync(c => c.Id == to); // not recorded
            var line = source.Lines[0];
            source.Lines.Remove(line);
            target.Lines.Add(line);

            var thrown = await Should.ThrowAsync<InvalidOperationException>(() => Commit(context));
            thrown.Message.ShouldContain("'CartLine' moves from 'Cart'");
            thrown.Message.ShouldContain(to.ToString());
        }

        (await StoredVersionAsync(db, from)).ShouldBe(1, "nothing was written");
        await using var verify = db.Create();
        (await verify.Set<CartLine>().CountAsync(l => EF.Property<CartId>(l, "CartId") == from)).ShouldBe(2);
    }

    [DockerRequiredFact]
    public async Task AChangedChildWhoseRootIsNotTracked_FailsTheSave()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);
        await using var context = db.Create();
        var line = await context.Set<CartLine>().FirstAsync(l => EF.Property<CartId>(l, "CartId") == id);
        line.Sku = "orphaned";

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => Commit(context));

        thrown.Message.ShouldContain("'CartLine' changed, but the root of its aggregate, 'Cart', is not tracked");
    }

    [DockerRequiredFact]
    public async Task AChildMovedIntoAnAggregateWhoseRootIsNotTracked_FailsTheSave()
    {
        await using var db = await CreateCartDatabaseAsync();
        var from = await SeedCartAsync(db);
        var to = await SeedCartAsync(db);

        await using (var context = db.Create())
        {
            var source = (await Carts(context).FindAsync(from)).ShouldNotBeNull();
            context.Entry(source.Lines[0]).Property("CartId").CurrentValue = to; // the target cart is not loaded

            var thrown = await Should.ThrowAsync<InvalidOperationException>(() => Commit(context));
            thrown.Message.ShouldContain("is not tracked");
        }

        (await StoredVersionAsync(db, to)).ShouldBe(1, "nothing was written");
    }

    [DockerRequiredFact]
    public async Task AChildTrackedAloneAndMovedIntoALoadedAggregate_FailsTheSave()
    {
        await using var db = await CreateCartDatabaseAsync();
        var from = await SeedCartAsync(db);
        var to = await SeedCartAsync(db);

        await using (var context = db.Create())
        {
            var line = await context.Set<CartLine>().FirstAsync(l => EF.Property<CartId>(l, "CartId") == from); // its root is not tracked
            var target = (await Carts(context).FindAsync(to)).ShouldNotBeNull();
            target.Lines.Add(line);

            var thrown = await Should.ThrowAsync<InvalidOperationException>(() => Commit(context));
            thrown.Message.ShouldContain("is not tracked");
        }

        (await StoredVersionAsync(db, from)).ShouldBe(1);
        (await StoredVersionAsync(db, to)).ShouldBe(1);
    }

    [DockerRequiredFact]
    public async Task ALinkThroughAJoinEntityWithAThirdForeignKey_BumpsTheRoot()
    {
        await using var db = await CreateDatabaseAsync<LabelledBasketDbContext>(o => new LabelledBasketDbContext(o));
        var basket = new Basket { Id = new CartId(Guid.NewGuid()) };
        await using (var context = db.Create())
        {
            await new EfRepository<Basket, CartId, LabelledBasketDbContext>(context).AddAsync(basket);
            await new EfUnitOfWork<LabelledBasketDbContext>(context).CommitAsync();
        }

        await using (var context = db.Create())
        {
            var loaded = (await new EfRepository<Basket, CartId, LabelledBasketDbContext>(context).FindAsync(basket.Id)).ShouldNotBeNull();
            loaded.Labels.Add(new BasketTag { Text = "fragile" });
            await new EfUnitOfWork<LabelledBasketDbContext>(context).CommitAsync();
        }

        await using var verify = db.Create();
        (await verify.Set<Basket>().Where(b => b.Id == basket.Id).Select(b => EF.Property<long>(b, AggregateVersion.PropertyName)).SingleAsync())
            .ShouldBe(2);
    }

    [DockerRequiredFact]
    public async Task AnInterceptorAddedByTheApplication_RunsAfterTheVersionStep()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);
        var probe = new VersionProbeInterceptor();
        await using var context = db.Create(o => o.AddInterceptors(probe));
        var cart = (await Carts(context).FindAsync(id)).ShouldNotBeNull();
        cart.Owner = "changed";

        await Commit(context);

        probe.SeenVersion.ShouldBe(2L, "MicroKit's step, an injected interceptor, runs before the application's own");
    }

    // ---- Entities outside every boundary -----------------------------------------------------

    [DockerRequiredFact]
    public async Task AnAggregateChangeAndAnUnrelatedEntityInOneSave_BumpTheRootOnce()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);

        await using (var context = db.Create())
        {
            var cart = (await Carts(context).FindAsync(id)).ShouldNotBeNull();
            cart.Owner = "changed";
            context.Add(new AuditRow { Text = "cart changed" });
            await Commit(context);
        }

        (await StoredVersionAsync(db, id)).ShouldBe(2);
        await using var verify = db.Create();
        (await verify.Set<AuditRow>().CountAsync()).ShouldBe(1);
    }

    [DockerRequiredFact]
    public async Task ASaveOfAnUnrelatedEntityOnly_BumpsNothing()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);

        await using (var context = db.Create())
        {
            _ = (await Carts(context).FindAsync(id)).ShouldNotBeNull();
            context.Add(new AuditRow { Text = "read only" });
            await Commit(context);
        }

        (await StoredVersionAsync(db, id)).ShouldBe(1);
    }

    // ---- Conflicts ---------------------------------------------------------------------------

    [DockerRequiredFact]
    public async Task TwoCommandsChangingDifferentChildren_Conflict_AndTheSecondWritesNothing()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);
        await using var first = db.Create();
        await using var second = db.Create();
        var mine = (await Carts(first).FindAsync(id)).ShouldNotBeNull();
        var theirs = (await Carts(second).FindAsync(id)).ShouldNotBeNull();

        mine.Lines.Single(l => l.Sku == "BOOK").Sku = "BOOK-first";
        theirs.Lines.Single(l => l.Sku == "PEN").Sku = "PEN-second";
        await Commit(first);
        var thrown = await Should.ThrowAsync<PersistenceException>(() => Commit(second));

        thrown.InnerException.ShouldBeOfType<DbUpdateConcurrencyException>();
        (await StoredVersionAsync(db, id)).ShouldBe(2);
        await using var verify = db.Create();
        (await verify.Set<CartLine>().AnyAsync(l => l.Sku == "PEN-second")).ShouldBeFalse("nothing of the second save is written");
        (await verify.Set<CartLine>().AnyAsync(l => l.Sku == "BOOK-first")).ShouldBeTrue();
    }

    [DockerRequiredFact]
    public async Task ARetriedAttempt_RecomputesTheVersionFromTheReloadedState()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);
        var failOnce = new FailingInsertInterceptor(failures: 1);
        await using var context = db.Create(o => o.AddInterceptors(failOnce), maxRetryCount: 3);
        var unitOfWork = new EfUnitOfWork<CartDbContext>(context);
        var attempt = new RetryState(Carts(context), unitOfWork, id);

        await unitOfWork.ExecuteAsync(
            static async (s, ct) =>
            {
                s.Attempts++;
                var cart = (await s.Carts.FindAsync(s.Id, ct)).ShouldNotBeNull();
                cart.Lines.Add(new CartLine { Sku = $"attempt-{s.Attempts}" });
                await s.UnitOfWork.CommitAsync(ct);
            },
            attempt);

        failOnce.Thrown.ShouldBe(1);
        attempt.Attempts.ShouldBe(2, "a real second attempt of the execution strategy");
        (await StoredVersionAsync(db, id)).ShouldBe(2, "the stored version moved up by exactly one");
    }

    // ---- Across requests: the expected version --------------------------------------------------

    [DockerRequiredFact]
    public async Task ACommandStatingAStaleVersion_Conflicts_AndWritesNothing()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);
        long etag;
        await using (var read = db.Create())
        {
            var carts = Carts(read);
            etag = carts.GetVersion((await carts.FindAsync(id)).ShouldNotBeNull());
        }

        await using (var concurrent = db.Create())
        {
            (await Carts(concurrent).FindAsync(id)).ShouldNotBeNull().Owner = "concurrent";
            await Commit(concurrent);
        }

        await using (var command = db.Create())
        {
            var cart = (await Carts(command).FindAsync(id, expectedVersion: etag)).ShouldNotBeNull();
            cart.Lines[0].Sku = "stale";
            var thrown = await Should.ThrowAsync<PersistenceException>(() => Commit(command));
            thrown.InnerException.ShouldBeOfType<DbUpdateConcurrencyException>();
        }

        (await StoredVersionAsync(db, id)).ShouldBe(etag + 1);
        await using var verify = db.Create();
        (await verify.Set<CartLine>().AnyAsync(l => l.Sku == "stale")).ShouldBeFalse();
    }

    [DockerRequiredFact]
    public async Task ACommandStatingTheCurrentVersion_Succeeds()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);

        await using (var command = db.Create())
        {
            var cart = (await Carts(command).FindAsync(id, expectedVersion: 1)).ShouldNotBeNull();
            cart.Lines[0].Sku = "fresh";
            await Commit(command);
        }

        (await StoredVersionAsync(db, id)).ShouldBe(2);
    }

    [DockerRequiredFact]
    public async Task AnExpectedVersionOnARootNotYetInserted_MustBeZero()
    {
        await using var db = await CreateCartDatabaseAsync();
        await using var context = db.Create();
        var carts = Carts(context);
        var cart = new Cart { Id = new CartId(Guid.NewGuid()), Owner = "new" };
        await carts.AddAsync(cart);

        var thrown = await Should.ThrowAsync<PersistenceException>(async () => await carts.FindAsync(cart.Id, expectedVersion: 5));
        thrown.InnerException.ShouldBeOfType<InvalidOperationException>().Message.ShouldContain("not inserted yet");
        (await carts.FindAsync(cart.Id, expectedVersion: 0)).ShouldBeSameAs(cart);
    }

    [DockerRequiredFact]
    public async Task ACommandStatingTheCurrentVersionButChangingNothing_WritesNothing()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);
        var commands = new List<string>();

        await using (var command = db.Create(o => o.LogTo(commands.Add, [Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuting])))
        {
            _ = (await Carts(command).FindAsync(id, expectedVersion: 1)).ShouldNotBeNull();
            commands.Clear();
            await Commit(command);
        }

        commands.ShouldBeEmpty("no change, no write");
        (await StoredVersionAsync(db, id)).ShouldBe(1);
    }

    [DockerRequiredFact]
    public async Task ACommandStatingAStaleVersionButChangingNothing_StillConflicts_AndWritesNothing()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);

        await using (var command = db.Create())
        {
            // The original value now differs from the loaded one: EF Core writes the root's token,
            // WHERE AggregateVersion = 0, which matches no row.
            _ = (await Carts(command).FindAsync(id, expectedVersion: 0)).ShouldNotBeNull();
            var thrown = await Should.ThrowAsync<PersistenceException>(() => Commit(command));
            thrown.InnerException.ShouldBeOfType<DbUpdateConcurrencyException>();
        }

        (await StoredVersionAsync(db, id)).ShouldBe(1);
    }

    [DockerRequiredFact]
    public async Task TheVersion_IsReadableFromAProjection()
    {
        await using var db = await CreateCartDatabaseAsync();
        var id = await SeedCartAsync(db);
        await using var context = db.Create();

        var summary = await context.Carts.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.Owner, Version = EF.Property<long>(c, AggregateVersion.PropertyName) })
            .SingleAsync();

        summary.Version.ShouldBe(1);
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static void Apply(CartDbContext context, Cart cart, CartChange change)
    {
        switch (change)
        {
            case CartChange.RootProperty: cart.Owner = "changed"; break;
            case CartChange.ChildProperty: cart.Lines[0].Sku = "changed"; break;
            case CartChange.GrandChildProperty: cart.Lines.Single(l => l.Options.Count > 0).Options[0].Name = "changed"; break;
            case CartChange.OwnedProperty: cart.Shipping!.City = "changed"; break;
            case CartChange.ChildAdded: cart.Lines.Add(new CartLine { Sku = "added" }); break;
            case CartChange.ChildDeleted: cart.Lines.Remove(cart.Lines.Single(l => l.Options.Count == 0)); break;
            case CartChange.ManyToManyLinkAdded: cart.Coupons.Add(new Coupon { Code = "NEW" }); break;
            case CartChange.PrincipalSideChildProperty: cart.PrimaryContact!.Name = "changed"; break;
            case CartChange.PrincipalSideChildReplaced:
                var old = cart.PrimaryContact!;
                cart.PrimaryContact = new CartContact { Name = "replacement" };
                context.Remove(old);
                break;
        }
    }

    private static EfRepository<Cart, CartId, CartDbContext> Carts(CartDbContext context) => new(context);

    private static Task Commit(DbContext context) =>
        context switch
        {
            RenamedVersionDbContext renamed => new EfUnitOfWork<RenamedVersionDbContext>(renamed).CommitAsync().AsTask(),
            CartDbContext carts => new EfUnitOfWork<CartDbContext>(carts).CommitAsync().AsTask(),
            _ => throw new NotSupportedException(),
        };

    private static async Task<long> StoredVersionAsync(Database<CartDbContext> db, CartId id)
    {
        await using var context = db.Create();
        return await context.Carts.AsNoTracking().Where(c => c.Id == id)
            .Select(c => EF.Property<long>(c, AggregateVersion.PropertyName)).SingleAsync();
    }

    private static async Task<CartId> SeedCartAsync(Database<CartDbContext> db)
    {
        var book = new CartLine { Sku = "BOOK" };
        book.Options.Add(new LineOption { Name = "gift wrap" });
        var cart = new Cart
        {
            Id = new CartId(Guid.NewGuid()),
            Owner = "Ada",
            Shipping = new CartAddress { City = "Lyon" },
            PrimaryContact = new CartContact { Name = "Grace" },
        };
        cart.Lines.Add(book);
        cart.Lines.Add(new CartLine { Sku = "PEN" });
        cart.Coupons.Add(new Coupon { Code = "WELCOME" });

        await using var context = db.Create();
        await Carts(context).AddAsync(cart);
        await Commit(context);
        return cart.Id;
    }

    private DbContextOptions<TContext> Options<TContext>(
        string database,
        Action<DbContextOptionsBuilder>? configure = null,
        int? maxRetryCount = null)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        UseProvider(builder, database, maxRetryCount);
        configure?.Invoke(builder);
        builder.UseMicroKitPersistence();
        return builder.Options;
    }

    private Task<Database<CartDbContext>> CreateCartDatabaseAsync() =>
        CreateDatabaseAsync<CartDbContext>(o => new CartDbContext(o));

    private async Task<Database<TContext>> CreateDatabaseAsync<TContext>(Func<DbContextOptions<TContext>, TContext> factory)
        where TContext : DbContext
    {
        var db = new Database<TContext>(this, "versions_" + Guid.NewGuid().ToString("N"), factory);
        await using var context = db.Create();
        await context.Database.EnsureCreatedAsync();
        return db;
    }

    /// <summary>One database of the provider, dropped on dispose.</summary>
    private sealed class Database<TContext>(
        AggregateVersionScenarios owner,
        string name,
        Func<DbContextOptions<TContext>, TContext> factory) : IAsyncDisposable
        where TContext : DbContext
    {
        public TContext Create(Action<DbContextOptionsBuilder>? configure = null, int? maxRetryCount = null) =>
            factory(owner.Options<TContext>(name, configure, maxRetryCount));

        public async ValueTask DisposeAsync()
        {
            await using var context = Create();
            await context.Database.EnsureDeletedAsync();
        }
    }

    private sealed class RetryState(EfRepository<Cart, CartId, CartDbContext> carts, EfUnitOfWork<CartDbContext> unitOfWork, CartId id)
    {
        public EfRepository<Cart, CartId, CartDbContext> Carts { get; } = carts;

        public EfUnitOfWork<CartDbContext> UnitOfWork { get; } = unitOfWork;

        public CartId Id { get; } = id;

        public int Attempts { get; set; }
    }
}

[Collection(PostgreSqlSuite.Name)]
public sealed class AggregateVersionPostgreSqlTests(PostgreSqlFixture fixture) : AggregateVersionScenarios
{
    protected override void UseProvider(DbContextOptionsBuilder builder, string database, int? maxRetryCount = null) =>
        builder.UseNpgsql(
            new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = database }.ConnectionString,
            npgsql =>
            {
                if (maxRetryCount is { } retries)
                    npgsql.ExecutionStrategy(d => new MarkerRetryingExecutionStrategy(d, retries));
            });
}

[Collection(SqlServerSuite.Name)]
public sealed class AggregateVersionSqlServerTests(SqlServerFixture fixture) : AggregateVersionScenarios
{
    protected override void UseProvider(DbContextOptionsBuilder builder, string database, int? maxRetryCount = null) =>
        builder.UseSqlServer(
            new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = database }.ConnectionString,
            sql =>
            {
                if (maxRetryCount is { } retries)
                    sql.ExecutionStrategy(d => new MarkerRetryingExecutionStrategy(d, retries));
            });
}

public readonly record struct CartId(Guid Value) : IEntityId;

/// <summary>
/// An aggregate with a child collection and a grand-child, an owned type in the root's table, a
/// many-to-many collection, and a contact the root references (the root holds the foreign key).
/// </summary>
public sealed class Cart : IAggregateRoot
{
    public CartId Id { get; init; }

    public string Owner { get; set; } = string.Empty;

    public List<CartLine> Lines { get; } = [];

    public CartAddress? Shipping { get; set; }

    public List<Coupon> Coupons { get; } = [];

    public Guid? PrimaryContactId { get; set; }

    public CartContact? PrimaryContact { get; set; }
}

public sealed class CartLine
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Sku { get; set; } = string.Empty;

    public List<LineOption> Options { get; } = [];
}

public sealed class LineOption
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
}

public sealed class CartAddress
{
    public string City { get; set; } = string.Empty;
}

public sealed class Coupon
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Code { get; set; } = string.Empty;
}

public sealed class CartContact
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
}

/// <summary>Outside every aggregate: an outbox-like row written in the same save.</summary>
public sealed class AuditRow
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Text { get; set; } = string.Empty;
}

public class CartDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Cart> Carts => Set<Cart>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Cart>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasConversion(id => id.Value, v => new CartId(v)).ValueGeneratedNever();
            b.HasMany(c => c.Lines).WithOne().HasForeignKey("CartId").OnDelete(DeleteBehavior.Cascade);
            b.OwnsOne(c => c.Shipping);
            b.HasMany(c => c.Coupons).WithMany();
            b.Navigation(c => c.Coupons).IsOutsideAggregate(); // many-to-many: coupons are reference data
            b.HasOne(c => c.PrimaryContact).WithOne().HasForeignKey<Cart>(c => c.PrimaryContactId); // one-to-one
        });
        modelBuilder.Entity<CartLine>(b =>
        {
            b.Property(l => l.Id).ValueGeneratedNever();
            b.HasMany(l => l.Options).WithOne().HasForeignKey("LineId");
        });
        modelBuilder.Entity<LineOption>().Property(o => o.Id).ValueGeneratedNever();
        modelBuilder.Entity<Coupon>().Property(c => c.Id).ValueGeneratedNever();
        modelBuilder.Entity<CartContact>().Property(c => c.Id).ValueGeneratedNever();
        modelBuilder.Entity<AuditRow>().Property(a => a.Id).ValueGeneratedNever();
    }
}

/// <summary>The cart model, with the version's column renamed by the application.</summary>
public sealed class RenamedVersionDbContext(DbContextOptions<RenamedVersionDbContext> options) : CartDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Cart>().Property<long>(AggregateVersion.PropertyName).HasColumnName("row_version");
    }
}

/// <summary>The cart model under a consumer naming convention (a stand-in for EFCore.NamingConventions).</summary>
public sealed class SnakeCaseCartDbContext(DbContextOptions<SnakeCaseCartDbContext> options) : CartDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Conventions.Add(_ => new SnakeCaseColumnConvention());

    private sealed class SnakeCaseColumnConvention : IPropertyAddedConvention
    {
        public void ProcessPropertyAdded(
            IConventionPropertyBuilder propertyBuilder,
            IConventionContext<IConventionPropertyBuilder> context)
        {
            // Owned types are left alone: unlike the real package, this stand-in does not keep a
            // table-split owned key on its owner's column.
            if (propertyBuilder.Metadata.DeclaringType is IConventionEntityType { } type && type.IsOwned())
                return;
            propertyBuilder.HasColumnName(string.Concat(propertyBuilder.Metadata.Name.Select(
                (ch, i) => i > 0 && char.IsUpper(ch) ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString())));
        }
    }
}

public enum VersionMisuse
{
    ClrMember,
    NotLong,
    NotAToken,
    SameColumn,
    OtherTable,
}

public sealed class ClrVersionRoot : IAggregateRoot
{
    public Guid Id { get; init; }

    public long AggregateVersion { get; set; }
}

public sealed class PlainVersionRoot : IAggregateRoot
{
    public Guid Id { get; init; }

    public long Other { get; set; }
}

public sealed class MisusedVersionDbContext(DbContextOptions<MisusedVersionDbContext> options, VersionMisuse misuse)
    : DbContext(options)
{
    public VersionMisuse Misuse { get; } = misuse;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        switch (Misuse)
        {
            case VersionMisuse.ClrMember:
                modelBuilder.Entity<ClrVersionRoot>();
                break;
            case VersionMisuse.NotLong:
                modelBuilder.Entity<PlainVersionRoot>().Property<int>(AggregateVersion.PropertyName);
                break;
            case VersionMisuse.NotAToken:
                modelBuilder.Entity<PlainVersionRoot>().Property<long>(AggregateVersion.PropertyName).IsConcurrencyToken(false);
                break;
            case VersionMisuse.SameColumn:
                modelBuilder.Entity<PlainVersionRoot>().Property(r => r.Other).HasColumnName(AggregateVersion.PropertyName);
                break;
            case VersionMisuse.OtherTable:
                modelBuilder.Entity<PlainVersionRoot>();
                modelBuilder.Entity<VersionedOutboxRow>(); // not a root: its own AggregateVersion, in its own table
                break;
        }
    }

    // One model per misuse: the model cache key includes it.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, MisuseCacheKeyFactory>();

    private sealed class MisuseCacheKeyFactory : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) =>
            (context.GetType(), ((MisusedVersionDbContext)context).Misuse, designTime);
    }
}

/// <summary>Not an aggregate: a row that happens to carry its own AggregateVersion column.</summary>
public sealed class VersionedOutboxRow
{
    public Guid Id { get; init; }

    public long AggregateVersion { get; set; }
}

/// <summary>An aggregate whose many-to-many link goes through an explicit join entity with a third foreign key.</summary>
public sealed class Basket : IAggregateRoot
{
    public CartId Id { get; init; }

    public List<BasketTag> Labels { get; } = [];
}

public sealed class BasketTag
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Text { get; set; } = string.Empty;
}

public sealed class Clerk
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

public sealed class BasketTagLink
{
    public CartId BasketId { get; set; }

    public Guid TagId { get; set; }

    public Guid? AddedById { get; set; }

    public Clerk? AddedBy { get; set; }
}

public sealed class LabelledBasketDbContext(DbContextOptions<LabelledBasketDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Basket>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasConversion(id => id.Value, v => new CartId(v)).ValueGeneratedNever();
            b.HasMany(x => x.Labels).WithMany().UsingEntity<BasketTagLink>(
                right => right.HasOne<BasketTag>().WithMany().HasForeignKey(l => l.TagId),
                left => left.HasOne<Basket>().WithMany().HasForeignKey(l => l.BasketId),
                join =>
                {
                    join.HasKey(l => new { l.BasketId, l.TagId });
                    join.Property(l => l.BasketId).HasConversion(id => id.Value, v => new CartId(v));
                    join.HasOne(l => l.AddedBy).WithMany().HasForeignKey(l => l.AddedById);
                });
        });
        modelBuilder.Entity<Basket>().Navigation(x => x.Labels).IsOutsideAggregate(); // many-to-many: reference data
        modelBuilder.Entity<BasketTag>().Property(t => t.Id).ValueGeneratedNever();
        modelBuilder.Entity<Clerk>().Property(c => c.Id).ValueGeneratedNever();
    }
}

/// <summary>Records the version the root holds when the application's own interceptor runs.</summary>
public sealed class VersionProbeInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    public long? SeenVersion { get; private set; }

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context!;
        var auto = context.ChangeTracker.AutoDetectChangesEnabled;
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        SeenVersion = context.ChangeTracker.Entries<Cart>().Single().Property<long>(AggregateVersion.PropertyName).CurrentValue;
        context.ChangeTracker.AutoDetectChangesEnabled = auto;
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

/// <summary>An aggregate whose child has a byte[] key, the key of a grand-child's foreign key.</summary>
public sealed class Archive : IAggregateRoot
{
    public CartId Id { get; init; }

    public List<ArchivedDocument> Documents { get; } = [];
}

public sealed class ArchivedDocument
{
    public byte[] Code { get; init; } = [];

    public string Title { get; set; } = string.Empty;

    public List<ArchivedPage> Pages { get; } = [];
}

public sealed class ArchivedPage
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Text { get; set; } = string.Empty;
}

public sealed class ArchiveDbContext(DbContextOptions<ArchiveDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Archive>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).HasConversion(id => id.Value, v => new CartId(v)).ValueGeneratedNever();
            b.HasMany(a => a.Documents).WithOne().HasForeignKey("ArchiveId");
        });
        modelBuilder.Entity<ArchivedDocument>(b =>
        {
            b.HasKey(d => d.Code);
            b.Property(d => d.Code).HasMaxLength(16).ValueGeneratedNever();
            b.HasMany(d => d.Pages).WithOne().HasForeignKey("DocumentCode");
        });
        modelBuilder.Entity<ArchivedPage>().Property(p => p.Id).ValueGeneratedNever();
    }
}
