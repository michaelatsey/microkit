using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Identifiers;
using MicroKit.Persistence.EntityFrameworkCore;
using MicroKit.Persistence.EntityFrameworkCore.PostgreSql;
using MicroKit.Persistence.EntityFrameworkCore.SqlServer;
using MicroKit.Persistence.IntegrationTests.PostgreSql;
using MicroKit.Persistence.IntegrationTests.SqlServer;
using Microsoft.Data.SqlClient;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// #197 phase 1: the aggregate boundary lives in the model, and a partial aggregate cannot be
/// tracked through a repository. Every scenario runs on PostgreSQL and on SQL Server.
/// </summary>
public abstract class AggregateBoundaryScenarios
{
    /// <summary>Configures the provider on <paramref name="database"/>.</summary>
    protected abstract void UseProvider(DbContextOptionsBuilder builder, string database);

    /// <summary>Registers <see cref="LibraryDbContext"/> pooled, on <paramref name="database"/>.</summary>
    protected abstract void RegisterPooled(EfCoreBuilder builder, string database);

    // ---- The boundary, in the model ---------------------------------------------------------

    [DockerRequiredFact]
    public void AChildReachedFromTwoRoots_FailsModelBuilding_NamingTheChildAndBothRoots()
    {
        using var context = new SharedChildDbContext(Options<SharedChildDbContext>("unused"));

        var thrown = Should.Throw<InvalidOperationException>(() => _ = context.Model);

        thrown.Message.ShouldContain("'SharedNote'");
        thrown.Message.ShouldContain("'AlphaRoot'");
        thrown.Message.ShouldContain("'BetaRoot'");
    }

    [DockerRequiredFact]
    public void ABaseTypeAndItsDerivedTypeReachedFromTwoRoots_FailsModelBuilding()
    {
        using var context = new OverlappingHierarchyDbContext(Options<OverlappingHierarchyDbContext>("unused"));

        var thrown = Should.Throw<InvalidOperationException>(() => _ = context.Model);

        thrown.Message.ShouldContain("'SpecialItem'");
        thrown.Message.ShouldContain("'GammaRoot'");
        thrown.Message.ShouldContain("'DeltaRoot'");
    }

    [DockerRequiredTheory]
    [InlineData(RowSharing.ManyToOneReference, "'RowSharingRoot.Contact'", "many-to-one reference",
        "make the target an aggregate root, or mark the navigation IsOutsideAggregate()")]
    [InlineData(RowSharing.ManyToMany, "'RowSharingRoot.Tags'", "many-to-many navigation",
        "an explicit link entity, a child of the root")]
    [InlineData(RowSharing.ManyToManyToARoot, "'RowSharingRoot.Owners'", "many-to-many navigation",
        "an explicit link entity, a child of the root")]
    public void ANavigationThroughWhichARowCouldBelongToSeveralAggregates_FailsModelBuilding(
        RowSharing shape,
        string navigation,
        string kind,
        string fix)
    {
        using var context = new RowSharingDbContext(Options<RowSharingDbContext>("unused"), shape);

        var thrown = Should.Throw<InvalidOperationException>(() => _ = context.Model);

        thrown.Message.ShouldContain(navigation);
        thrown.Message.ShouldContain(kind);
        thrown.Message.ShouldContain(fix);
    }

    [DockerRequiredFact]
    public void AOneToOneReferenceHeldByTheParent_StaysInTheBoundary()
    {
        using var context = new RowSharingDbContext(Options<RowSharingDbContext>("unused"), RowSharing.OneToOne);

        Should.NotThrow(() => _ = context.Model);
        AggregateIncludePlanPathsOf(context).ShouldContain("Contact");
    }

    [DockerRequiredTheory]
    [InlineData(RowSharing.ManyToOneOutside)]
    [InlineData(RowSharing.ManyToManyOutside)]
    [InlineData(RowSharing.ManyToOneToARoot)]
    [InlineData(RowSharing.ManyToManyToARootOutside)]
    public void ASharedShapeMarkedOutsideOrReachingARoot_Builds(RowSharing shape)
    {
        using var context = new RowSharingDbContext(Options<RowSharingDbContext>("unused"), shape);

        Should.NotThrow(() => _ = context.Model);
    }

    [DockerRequiredFact]
    public void WithoutUseMicroKitPersistence_ASharedChildIsNotValidated()
    {
        var builder = new DbContextOptionsBuilder<SharedChildDbContext>();
        UseProvider(builder, "unused");
        using var context = new SharedChildDbContext(builder.Options);

        Should.NotThrow(() => _ = context.Model);
    }

    [DockerRequiredFact]
    public async Task TheSameOwnedTypeUnderTwoRoots_IsNotShared_AndIsLoadedWithEach()
    {
        await using var db = await CreateDatabaseAsync();
        var (shelfId, readerId) = await db.SeedAsync();
        await using var context = db.CreateContext();

        var shelf = (await new EfRepository<Shelf, ShelfId, LibraryDbContext>(context).FindAsync(shelfId)).ShouldNotBeNull();
        var reader = (await new EfRepository<Reader, ReaderId, LibraryDbContext>(context).FindAsync(readerId)).ShouldNotBeNull();

        shelf.Location.ShouldNotBeNull().City.ShouldBe("Lyon");
        reader.Home.ShouldNotBeNull().City.ShouldBe("Paris");
    }

    [DockerRequiredFact]
    public async Task ANavigationMarkedOutsideTheAggregate_IsNeitherLoadedNorCountedAsShared()
    {
        await using var db = await CreateDatabaseAsync();
        var (shelfId, readerId) = await db.SeedAsync();
        await using var context = db.CreateContext();

        // Genre is reached from Shelf (Books.Genre, marked outside) and from Reader: the model builds.
        var shelf = (await new EfRepository<Shelf, ShelfId, LibraryDbContext>(context).FindAsync(shelfId)).ShouldNotBeNull();

        var novel = shelf.Books.Single(b => b.Title == "Novel");
        novel.GenreId.ShouldNotBeNull();
        novel.Genre.ShouldBeNull("marked IsOutsideAggregate(): never loaded with the shelf");
        context.ChangeTracker.Entries<Genre>().ShouldBeEmpty();

        var reader = (await new EfRepository<Reader, ReaderId, LibraryDbContext>(context).FindAsync(readerId)).ShouldNotBeNull();
        reader.FavouriteGenre.ShouldNotBeNull("inside the Reader boundary: loaded with it");
    }

    [DockerRequiredFact]
    public async Task WithoutUseMicroKitPersistence_FindAsync_ThrowsAnExplicitError()
    {
        await using var db = await CreateDatabaseAsync();
        var (shelfId, _) = await db.SeedAsync();
        await using var context = db.CreateContext(useMicroKit: false);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await new EfRepository<Shelf, ShelfId, LibraryDbContext>(context).FindAsync(shelfId));

        thrown.Message.ShouldContain("UseMicroKitPersistence()");
    }

    // ---- A partial root is refused --------------------------------------------------------

    [DockerRequiredFact]
    public async Task ARootTrackedThroughAFilteredInclude_IsRefused()
    {
        await using var db = await CreateDatabaseAsync();
        var (shelfId, _) = await db.SeedAsync();
        await using var context = db.CreateContext();
        var partial = await context.Shelves.Include(s => s.Books.Where(b => b.Title == "Novel")).SingleAsync(s => s.Id == shelfId);
        context.Entry(partial).Collection(s => s.Books).IsLoaded.ShouldBeTrue("IsLoaded lies about a filtered Include");

        await ShouldRefuseAsync(new EfRepository<Shelf, ShelfId, LibraryDbContext>(context), shelfId);
    }

    [DockerRequiredFact]
    public async Task ARootTrackedThroughAPartialExplicitLoad_IsRefused()
    {
        await using var db = await CreateDatabaseAsync();
        var (shelfId, _) = await db.SeedAsync();
        await using var context = db.CreateContext();
        var shelf = await context.Shelves.AsNoTracking().SingleAsync(s => s.Id == shelfId);
        context.Attach(shelf);
        await context.Entry(shelf).Collection(s => s.Books).Query().Where(b => b.Title == "Novel").LoadAsync();

        await ShouldRefuseAsync(new EfRepository<Shelf, ShelfId, LibraryDbContext>(context), shelfId);
    }

    [DockerRequiredFact]
    public async Task ARootTrackedThroughADirectDbSetQuery_IsRefused()
    {
        await using var db = await CreateDatabaseAsync();
        var (shelfId, _) = await db.SeedAsync();
        await using var context = db.CreateContext();
        _ = await context.Shelves.SingleAsync(s => s.Id == shelfId);

        await ShouldRefuseAsync(new EfRepository<Shelf, ShelfId, LibraryDbContext>(context), shelfId);
    }

    [DockerRequiredTheory]
    [InlineData("attach")]
    [InlineData("update")]
    public async Task ARootLoadedByTheRepository_ThenClearedAndReattached_IsRefused(string reattach)
    {
        await using var db = await CreateDatabaseAsync();
        var (shelfId, _) = await db.SeedAsync();
        await using var context = db.CreateContext();
        var sut = new EfRepository<Shelf, ShelfId, LibraryDbContext>(context);
        var shelf = (await sut.FindAsync(shelfId)).ShouldNotBeNull();

        context.ChangeTracker.Clear();
        if (reattach == "attach")
            context.Attach(shelf);
        else
            context.Update(shelf);

        await ShouldRefuseAsync(sut, shelfId);
    }

    [DockerRequiredFact]
    public async Task ARootIdentityResolvedByLoadAggregatesAsync_IsRefused()
    {
        await using var db = await CreateDatabaseAsync();
        var (shelfId, _) = await db.SeedAsync();
        await using var context = db.CreateContext();
        var partial = await context.Shelves.SingleAsync(s => s.Id == shelfId);

        // The query hands back the tracked instance without raising Tracked: it was never recorded.
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await new EfShelfRepository(context).FindByNameAsync("Fiction"));

        thrown.Message.ShouldContain("'Shelf'");
        context.Entry(partial).State.ShouldBe(EntityState.Unchanged);
    }

    // ---- What is returned -----------------------------------------------------------------

    [DockerRequiredFact]
    public async Task ARecordedRoot_IsReturnedAsIs()
    {
        await using var db = await CreateDatabaseAsync();
        var (shelfId, _) = await db.SeedAsync();
        await using var context = db.CreateContext();
        var sut = new EfRepository<Shelf, ShelfId, LibraryDbContext>(context);
        var first = (await sut.FindAsync(shelfId)).ShouldNotBeNull();
        first.Name = "renamed";

        var second = await sut.FindAsync(shelfId);

        second.ShouldBeSameAs(first);
        context.Entry(first).State.ShouldBe(EntityState.Modified);
    }

    [DockerRequiredFact]
    public async Task AnAddedRoot_IsReturned()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.CreateContext();
        var sut = new EfRepository<Shelf, ShelfId, LibraryDbContext>(context);
        var shelf = new Shelf { Id = new ShelfId(Guid.NewGuid()), Name = "new" };
        await sut.AddAsync(shelf);

        (await sut.FindAsync(shelf.Id)).ShouldBeSameAs(shelf);
    }

    [DockerRequiredFact]
    public async Task LoadAggregatesAsync_RecordsWhatItLoads_ButNotAnotherRootItIncludes()
    {
        await using var db = await CreateDatabaseAsync();
        var (shelfId, readerId) = await db.SeedAsync();
        await using var context = db.CreateAutoIncludeContext();
        var shelves = new EfShelfRepository(context);

        var shelf = (await shelves.FindByNameAsync("Fiction")).ShouldNotBeNull();

        shelf.Books.Count.ShouldBe(2, "the whole aggregate");
        shelf.Curator.ShouldNotBeNull("auto-included by the model, so tracked during the load");
        (await shelves.FindAsync(shelfId)).ShouldBeSameAs(shelf, "the shelf was recorded");
        await ShouldRefuseAsync(new EfRepository<Reader, ReaderId, LibraryDbContext>(context), readerId, "'Reader'");
    }

    // ---- The record is reset ----------------------------------------------------------------

    [DockerRequiredFact]
    public async Task OnAPooledContext_ARootLoadedOnTheNextLease_ThenClearedAndReattached_IsRefused()
    {
        await using var db = await CreateDatabaseAsync();
        var (shelfId, _) = await db.SeedAsync();
        await using var root = BuildPooledProvider(db.Name);

        LibraryDbContext firstLease;
        using (var first = root.CreateScope())
        {
            firstLease = first.ServiceProvider.GetRequiredService<LibraryDbContext>();
            (await new EfRepository<Shelf, ShelfId, LibraryDbContext>(firstLease).FindAsync(shelfId)).ShouldNotBeNull();
        }

        using var next = root.CreateScope();
        var context = next.ServiceProvider.GetRequiredService<LibraryDbContext>();
        context.ShouldBeSameAs(firstLease, "the context is pooled");
        var sut = new EfRepository<Shelf, ShelfId, LibraryDbContext>(context);

        // Returning to the pool dropped every change-tracker handler: the load must subscribe again.
        var shelf = (await sut.FindAsync(shelfId)).ShouldNotBeNull();
        context.ChangeTracker.Clear();
        context.Attach(shelf);

        await ShouldRefuseAsync(sut, shelfId);
    }

    [DockerRequiredFact]
    public async Task OnAPooledContext_ARootRecordedOnAPreviousLease_AttachedOnTheNext_IsRefused()
    {
        await using var db = await CreateDatabaseAsync();
        var (shelfId, _) = await db.SeedAsync();
        await using var root = BuildPooledProvider(db.Name);

        LibraryDbContext firstLease;
        Shelf loaded;
        using (var first = root.CreateScope())
        {
            firstLease = first.ServiceProvider.GetRequiredService<LibraryDbContext>();
            loaded = (await new EfRepository<Shelf, ShelfId, LibraryDbContext>(firstLease).FindAsync(shelfId)).ShouldNotBeNull();
        }

        using var next = root.CreateScope();
        var context = next.ServiceProvider.GetRequiredService<LibraryDbContext>();
        context.ShouldBeSameAs(firstLease, "the context is pooled");

        // No handler observes this attach (the pool dropped them): only the record's reset protects it.
        context.Attach(loaded);

        await ShouldRefuseAsync(new EfRepository<Shelf, ShelfId, LibraryDbContext>(context), shelfId);
    }

    [DockerRequiredFact]
    public async Task AfterDiscardChanges_TheSameInstanceReattached_IsRefused()
    {
        await using var db = await CreateDatabaseAsync();
        var (shelfId, _) = await db.SeedAsync();
        await using var context = db.CreateContext();
        var sut = new EfRepository<Shelf, ShelfId, LibraryDbContext>(context);
        var shelf = (await sut.FindAsync(shelfId)).ShouldNotBeNull();

        new EfUnitOfWork<LibraryDbContext>(context).DiscardChanges();
        context.Attach(shelf);

        await ShouldRefuseAsync(sut, shelfId);
    }

    [DockerRequiredFact]
    public async Task AfterAFailedExecuteAsyncAttempt_TheSameInstanceReattached_IsRefused()
    {
        await using var db = await CreateDatabaseAsync();
        var (shelfId, _) = await db.SeedAsync();
        await using var context = db.CreateContext();
        var sut = new EfRepository<Shelf, ShelfId, LibraryDbContext>(context);
        var unitOfWork = new EfUnitOfWork<LibraryDbContext>(context);
        var attempt = new AttemptState(sut, shelfId);

        await Should.ThrowAsync<AttemptFailedException>(() => unitOfWork.ExecuteAsync(
            static async (s, ct) =>
            {
                s.Loaded = await s.Repository.FindAsync(s.Id, ct);
                throw new AttemptFailedException();
            },
            attempt));

        context.ChangeTracker.Entries().ShouldBeEmpty("the failed attempt cleared the tracker");
        context.Attach(attempt.Loaded.ShouldNotBeNull());

        await ShouldRefuseAsync(sut, shelfId);
    }

    // ---- The read side never tracks ---------------------------------------------------------

    [DockerRequiredFact]
    public async Task ReadFindAsync_WithAPartialRootTracked_ReturnsANewUntrackedInstance_AndLeavesTheTrackedOneAlone()
    {
        await using var db = await CreateDatabaseAsync();
        var (shelfId, _) = await db.SeedAsync();
        await using var context = db.CreateContext();
        var partial = await context.Shelves.SingleAsync(s => s.Id == shelfId);
        partial.Name = "pending";

        var found = (await ReadRepository(context).FindAsync([shelfId])).ShouldNotBeNull();

        found.ShouldNotBeSameAs(partial);
        context.Entry(found).State.ShouldBe(EntityState.Detached);
        found.Name.ShouldBe("Fiction", "read from the database, not from the tracker");
        context.Entry(partial).State.ShouldBe(EntityState.Modified);
        partial.Name.ShouldBe("pending");
    }

    [DockerRequiredFact]
    public async Task ReadListAsync_WithAPartialRootTracked_ReturnsNewUntrackedInstances_AndLeavesTheTrackedOneAlone()
    {
        await using var db = await CreateDatabaseAsync();
        var (shelfId, _) = await db.SeedAsync();
        await using var context = db.CreateContext();
        var partial = await context.Shelves.SingleAsync(s => s.Id == shelfId);
        partial.Name = "pending";

        var found = (await ReadRepository(context).ListAsync(new QueryOptions<Shelf>())).ShouldHaveSingleItem();

        found.ShouldNotBeSameAs(partial);
        context.Entry(found).State.ShouldBe(EntityState.Detached);
        context.Entry(partial).State.ShouldBe(EntityState.Modified);
        partial.Name.ShouldBe("pending");
    }

    [DockerRequiredFact]
    public async Task ReadListAsync_WhenTheIncludesAskForTracking_StillTracksNothing()
    {
        await using var db = await CreateDatabaseAsync();
        await db.SeedAsync();
        await using var context = db.CreateContext();
        var opts = new QueryOptions<Shelf>().WithIncludes(q => q.AsTracking().Include(s => s.Books));

        var found = (await ReadRepository(context).ListAsync(opts)).ShouldHaveSingleItem();
        var page = await ReadRepository(context).ListPagedAsync(opts.WithOrdering(q => q.OrderBy(s => s.Name)));

        found.Books.Count.ShouldBe(2);
        page.Items.ShouldHaveSingleItem();
        context.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [DockerRequiredFact]
    public void AnOwnedNavigationMarkedOutsideTheAggregate_FailsModelBuilding()
    {
        using var context = new OwnedOutsideDbContext(Options<OwnedOutsideDbContext>("unused"));

        var thrown = Should.Throw<InvalidOperationException>(() => _ = context.Model);

        thrown.Message.ShouldContain("'Address'");
        thrown.Message.ShouldContain("IsOutsideAggregate()");
    }

    [DockerRequiredFact]
    public async Task AnAddedRoot_AfterTheCommit_IsStillReturnedAsIs()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.CreateContext();
        var sut = new EfRepository<Shelf, ShelfId, LibraryDbContext>(context);
        var shelf = new Shelf { Id = new ShelfId(Guid.NewGuid()), Name = "new" };
        shelf.Books.Add(new Book { Title = "Draft" });
        await sut.AddAsync(shelf);
        await new EfUnitOfWork<LibraryDbContext>(context).CommitAsync();
        context.Entry(shelf).State.ShouldBe(EntityState.Unchanged);

        (await sut.FindAsync(shelf.Id)).ShouldBeSameAs(shelf, "the application built it whole and the repository staged it");
    }

    [DockerRequiredFact]
    public async Task ARootAddedDirectlyOnTheContext_IsReturnedWhileAdded_AndRefusedOnceCommitted()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.CreateContext();
        var sut = new EfRepository<Shelf, ShelfId, LibraryDbContext>(context);
        var shelf = new Shelf { Id = new ShelfId(Guid.NewGuid()), Name = "direct" };
        context.Add(shelf); // not EfRepository.AddAsync: never recorded (ADR-009)

        (await sut.FindAsync(shelf.Id)).ShouldBeSameAs(shelf, "an Added root is returned");

        await new EfUnitOfWork<LibraryDbContext>(context).CommitAsync();
        context.Entry(shelf).State.ShouldBe(EntityState.Unchanged);

        await ShouldRefuseAsync(sut, shelf.Id);
    }

    [DockerRequiredFact]
    public async Task ARootWhoseLoadFailedMidway_IsRefused()
    {
        await using var db = await CreateDatabaseAsync();
        var (shelfId, _) = await db.SeedAsync();
        var interceptor = new FailingBookQueryInterceptor();
        await using var context = db.CreateContext(configure: o => o.AddInterceptors(interceptor));
        var sut = new EfRepository<Shelf, ShelfId, LibraryDbContext>(context);

        // Two collections split the load: the root query tracks the shelf, then the books query fails.
        interceptor.Armed = true;
        await Should.ThrowAsync<LoadFailedException>(async () => await sut.FindAsync(shelfId));
        context.ChangeTracker.Entries<Shelf>().ShouldHaveSingleItem("the root was tracked before the failure");

        await ShouldRefuseAsync(sut, shelfId);
    }

    [DockerRequiredFact]
    public async Task ARootOfTheLoadedHierarchyAutoIncluded_IsNotRecorded()
    {
        await using var db = await CreateDatabaseAsync();
        await using (var seed = db.CreatePetContext())
            await seed.Database.EnsureCreatedAsync();

        var cat = new Cat { Id = new PetId(Guid.NewGuid()), Name = "Tom" };
        cat.Toys.Add(new Toy { Label = "ball" });
        var dog = new Dog { Id = new PetId(Guid.NewGuid()), Name = "Rex", FriendId = cat.Id };
        await using (var seed = db.CreatePetContext())
        {
            seed.AddRange(cat, dog);
            await seed.SaveChangesAsync();
        }

        try
        {
            await using var context = db.CreatePetContext();
            var pets = new EfRepository<Pet, PetId, PetDbContext>(context);

            var loaded = (await pets.FindAsync(dog.Id)).ShouldBeOfType<Dog>();

            loaded.Friend.ShouldNotBeNull("auto-included: a Pet tracked from the query, but not one the query returned");
            loaded.Friend.Toys.ShouldBeEmpty("its own aggregate was not loaded");
            var thrown = await Should.ThrowAsync<InvalidOperationException>(async () => await pets.FindAsync(cat.Id));
            thrown.Message.ShouldContain("did not load it whole");
        }
        finally
        {
            await using var drop = db.CreatePetContext();
            await drop.Database.EnsureDeletedAsync();
        }
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static async Task ShouldRefuseAsync<TAggregate, TId>(
        EfRepository<TAggregate, TId, LibraryDbContext> repository,
        TId id,
        string rootName = "'Shelf'")
        where TAggregate : class, IAggregateRoot
        where TId : IEntityId
    {
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () => await repository.FindAsync(id));
        thrown.Message.ShouldContain(rootName);
        thrown.Message.ShouldContain("did not load it whole");
    }

    // The include paths the write side applies: read through a load's SQL would need a database.
    private static List<string> AggregateIncludePlanPathsOf(RowSharingDbContext context) =>
        context.Model.FindEntityType(typeof(RowSharingRoot))!.GetNavigations()
            .Where(n => n.FindAnnotation("MicroKit:OutsideAggregate") is null)
            .Select(n => n.Name)
            .ToList();

    private static EfReadRepository<Shelf, LibraryDbContext> ReadRepository(LibraryDbContext context) =>
        new(context, new EfSpecificationEvaluator(), PaginationLimits.Default);

    private DbContextOptions<TContext> Options<TContext>(
        string database,
        bool useMicroKit = true,
        Action<DbContextOptionsBuilder>? configure = null)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        UseProvider(builder, database);
        configure?.Invoke(builder);
        if (useMicroKit)
            builder.UseMicroKitPersistence();
        return builder.Options;
    }

    private ServiceProvider BuildPooledProvider(string database)
    {
        var services = new ServiceCollection();
        services.AddMicroKitPersistence(p => p.AddEntityFrameworkCore(ef => RegisterPooled(ef, database)));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private async Task<LibraryDatabase> CreateDatabaseAsync()
    {
        var db = new LibraryDatabase(this, "library_" + Guid.NewGuid().ToString("N"));
        await using var context = db.CreateContext();
        await context.Database.EnsureCreatedAsync();
        return db;
    }

    /// <summary>One database of the provider, dropped on dispose.</summary>
    private sealed class LibraryDatabase(AggregateBoundaryScenarios owner, string name) : IAsyncDisposable
    {
        public string Name { get; } = name;

        public LibraryDbContext CreateContext(bool useMicroKit = true, Action<DbContextOptionsBuilder>? configure = null) =>
            new(owner.Options<LibraryDbContext>(Name, useMicroKit, configure));

        public PetDbContext CreatePetContext() => new(owner.Options<PetDbContext>(Name + "_pets"));

        public AutoIncludeLibraryDbContext CreateAutoIncludeContext() =>
            new(owner.Options<AutoIncludeLibraryDbContext>(Name));

        public async Task<(ShelfId Shelf, ReaderId Reader)> SeedAsync()
        {
            var poetry = new Genre { Name = "poetry" };
            var reader = new Reader
            {
                Id = new ReaderId(Guid.NewGuid()),
                Name = "Ada",
                Home = new Address { City = "Paris" },
                FavouriteGenre = poetry,
            };
            var shelf = new Shelf
            {
                Id = new ShelfId(Guid.NewGuid()),
                Name = "Fiction",
                Location = new Address { City = "Lyon" },
                CuratorId = reader.Id,
            };
            shelf.Books.Add(new Book { Title = "Novel", GenreId = poetry.Id });
            shelf.Books.Add(new Book { Title = "Essay" });
            shelf.Labels.Add(new ShelfLabel { Text = "staff pick" });

            await using var context = CreateContext();
            context.AddRange(reader, shelf);
            await context.SaveChangesAsync();
            return (shelf.Id, reader.Id);
        }

        public async ValueTask DisposeAsync()
        {
            await using var context = CreateContext();
            await context.Database.EnsureDeletedAsync();
        }
    }

    /// <summary>Threads the loaded instance out of a failed <c>ExecuteAsync</c> attempt.</summary>
    private sealed class AttemptState(EfRepository<Shelf, ShelfId, LibraryDbContext> repository, ShelfId id)
    {
        public EfRepository<Shelf, ShelfId, LibraryDbContext> Repository { get; } = repository;

        public ShelfId Id { get; } = id;

        public Shelf? Loaded { get; set; }
    }

    private sealed class AttemptFailedException() : Exception("the attempt failed");

    private sealed class LoadFailedException() : Exception("the books query failed");

    /// <summary>Fails the next command that reads books, once armed.</summary>
    private sealed class FailingBookQueryInterceptor : DbCommandInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("Book", StringComparison.Ordinal))
            {
                Armed = false;
                throw new LoadFailedException();
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}

[Collection(PostgreSqlSuite.Name)]
public sealed class AggregateBoundaryPostgreSqlTests(PostgreSqlFixture fixture) : AggregateBoundaryScenarios
{
    protected override void UseProvider(DbContextOptionsBuilder builder, string database) =>
        builder.UseNpgsql(ConnectionString(database));

    protected override void RegisterPooled(EfCoreBuilder builder, string database) =>
        builder.UsePostgreSQL<LibraryDbContext>(ConnectionString(database), options: new ProviderOptions { PoolSize = 4 });

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = database }.ConnectionString;
}

[Collection(SqlServerSuite.Name)]
public sealed class AggregateBoundarySqlServerTests(SqlServerFixture fixture) : AggregateBoundaryScenarios
{
    protected override void UseProvider(DbContextOptionsBuilder builder, string database) =>
        builder.UseSqlServer(ConnectionString(database));

    protected override void RegisterPooled(EfCoreBuilder builder, string database) =>
        builder.UseSqlServer<LibraryDbContext>(ConnectionString(database), options: new ProviderOptions { PoolSize = 4 });

    private string ConnectionString(string database) =>
        new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = database }.ConnectionString;
}

/// <summary>A typed repository whose finder goes through <c>LoadAggregatesAsync</c>.</summary>
public sealed class EfShelfRepository(LibraryDbContext context)
    : EfRepository<Shelf, ShelfId, LibraryDbContext>(context)
{
    public async ValueTask<Shelf?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        var shelves = await LoadAggregatesAsync(s => s.Name == name, ct).ConfigureAwait(false);
        return shelves.SingleOrDefault();
    }
}

public readonly record struct ShelfId(Guid Value) : IEntityId;

public readonly record struct ReaderId(Guid Value) : IEntityId;

/// <summary>An aggregate root: its books, its owned location; the curator is another aggregate.</summary>
public sealed class Shelf : IAggregateRoot
{
    public ShelfId Id { get; init; }

    public string Name { get; set; } = string.Empty;

    public List<Book> Books { get; } = [];

    /// <summary>A second collection: the whole-aggregate load is split.</summary>
    public List<ShelfLabel> Labels { get; } = [];

    public Address? Location { get; set; }

    public ReaderId? CuratorId { get; set; }

    public Reader? Curator { get; set; }
}

public sealed class Book
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public Guid? GenreId { get; set; }

    /// <summary>Reference data: marked <c>IsOutsideAggregate()</c>.</summary>
    public Genre? Genre { get; set; }
}

public sealed class ShelfLabel
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Text { get; set; } = string.Empty;
}

/// <summary>Reached from <see cref="Shelf"/> (outside) and from <see cref="Reader"/> (inside).</summary>
public sealed class Genre
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
}

/// <summary>Another aggregate root, owning an <see cref="Address"/> too.</summary>
public sealed class Reader : IAggregateRoot
{
    public ReaderId Id { get; init; }

    public string Name { get; set; } = string.Empty;

    public Address? Home { get; set; }

    public Genre? FavouriteGenre { get; set; }
}

/// <summary>Owned by <see cref="Shelf"/> and by <see cref="Reader"/>: two entity types, not shared.</summary>
public sealed class Address
{
    public string City { get; set; } = string.Empty;
}

public class LibraryDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Shelf> Shelves => Set<Shelf>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shelf>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasConversion(id => id.Value, v => new ShelfId(v)).ValueGeneratedNever();
            b.Property(s => s.CuratorId).HasConversion(new ValueConverter<ReaderId, Guid>(id => id.Value, v => new ReaderId(v)));
            b.HasMany(s => s.Books).WithOne().HasForeignKey("ShelfId");
            b.HasMany(s => s.Labels).WithOne().HasForeignKey("ShelfId");
            b.HasOne(s => s.Curator).WithMany().HasForeignKey(s => s.CuratorId);
            b.OwnsOne(s => s.Location);
        });

        modelBuilder.Entity<Book>(b =>
        {
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasOne(x => x.Genre).WithMany().HasForeignKey(x => x.GenreId);
            b.Navigation(x => x.Genre).IsOutsideAggregate();
        });

        modelBuilder.Entity<Genre>().Property(g => g.Id).ValueGeneratedNever();
        modelBuilder.Entity<ShelfLabel>().Property(l => l.Id).ValueGeneratedNever();

        modelBuilder.Entity<Reader>(b =>
        {
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasConversion(id => id.Value, v => new ReaderId(v)).ValueGeneratedNever();
            b.HasOne(r => r.FavouriteGenre).WithOne().HasForeignKey<Reader>("FavouriteGenreId");
            b.OwnsOne(r => r.Home);
        });
    }
}

/// <summary>The same model, with the curator — another aggregate root — auto-included.</summary>
public sealed class AutoIncludeLibraryDbContext(DbContextOptions<AutoIncludeLibraryDbContext> options)
    : LibraryDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Shelf>().Navigation(s => s.Curator).AutoInclude();
    }
}

public sealed class AlphaRoot : IAggregateRoot
{
    public Guid Id { get; init; }

    public List<SharedNote> Notes { get; } = [];
}

public sealed class BetaRoot : IAggregateRoot
{
    public Guid Id { get; init; }

    public List<SharedNote> Notes { get; } = [];
}

/// <summary>A child reached from two roots.</summary>
public sealed class SharedNote
{
    public Guid Id { get; init; }
}

public sealed class SharedChildDbContext(DbContextOptions<SharedChildDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AlphaRoot>().HasMany(a => a.Notes).WithOne().HasForeignKey("AlphaId");
        modelBuilder.Entity<BetaRoot>().HasMany(b => b.Notes).WithOne().HasForeignKey("BetaId");
    }
}

public sealed class GammaRoot : IAggregateRoot
{
    public Guid Id { get; init; }

    public List<Item> Items { get; } = [];
}

public sealed class DeltaRoot : IAggregateRoot
{
    public Guid Id { get; init; }

    public List<SpecialItem> Specials { get; } = [];
}

public class Item
{
    public Guid Id { get; init; }
}

/// <summary>Its rows are <see cref="Item"/> rows: reached through both roots.</summary>
public sealed class SpecialItem : Item;

public sealed class OverlappingHierarchyDbContext(DbContextOptions<OverlappingHierarchyDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GammaRoot>().HasMany(g => g.Items).WithOne().HasForeignKey("GammaId");
        modelBuilder.Entity<DeltaRoot>().HasMany(d => d.Specials).WithOne().HasForeignKey("DeltaId");
    }
}

/// <summary>An owned navigation marked outside the aggregate: its own children would be skipped.</summary>
public sealed class OwnedOutsideDbContext(DbContextOptions<OwnedOutsideDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Reader>(b =>
        {
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasConversion(id => id.Value, v => new ReaderId(v));
            b.Ignore(r => r.FavouriteGenre);
            b.OwnsOne(r => r.Home);
            b.Navigation(r => r.Home).IsOutsideAggregate();
        });
}

public readonly record struct PetId(Guid Value) : IEntityId;

/// <summary>A root hierarchy (TPH): a dog auto-includes its friend, a cat, another aggregate of the same hierarchy.</summary>
public class Pet : IAggregateRoot
{
    public PetId Id { get; init; }

    public string Name { get; set; } = string.Empty;
}

public sealed class Dog : Pet
{
    public PetId? FriendId { get; set; }

    public Cat? Friend { get; set; }
}

public sealed class Cat : Pet
{
    public List<Toy> Toys { get; } = [];
}

public sealed class Toy
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Label { get; set; } = string.Empty;
}

public sealed class PetDbContext(DbContextOptions<PetDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Pet>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).HasConversion(id => id.Value, v => new PetId(v)).ValueGeneratedNever();
        });
        modelBuilder.Entity<Dog>(b =>
        {
            b.Property(d => d.FriendId).HasConversion(new ValueConverter<PetId, Guid>(id => id.Value, v => new PetId(v)));
            b.HasOne(d => d.Friend).WithMany().HasForeignKey(d => d.FriendId).OnDelete(DeleteBehavior.NoAction);
            b.Navigation(d => d.Friend).AutoInclude();
        });
        modelBuilder.Entity<Cat>().HasMany(c => c.Toys).WithOne().HasForeignKey("CatId");
        modelBuilder.Entity<Toy>().Property(t => t.Id).ValueGeneratedNever();
    }
}

public enum RowSharing
{
    ManyToOneReference,
    ManyToMany,
    OneToOne,
    ManyToOneOutside,
    ManyToManyOutside,
    ManyToOneToARoot,
    ManyToManyToARoot,
    ManyToManyToARootOutside,
}

public sealed class RowSharingRoot : IAggregateRoot
{
    public Guid Id { get; init; }

    public Guid? ContactId { get; set; }

    public RowSharingContact? Contact { get; set; }

    public Guid? OwnerId { get; set; }

    public RowSharingOwner? Owner { get; set; }

    public List<RowSharingTag> Tags { get; } = [];

    public List<RowSharingOwner> Owners { get; } = [];
}

public sealed class RowSharingContact
{
    public Guid Id { get; init; }
}

public sealed class RowSharingTag
{
    public Guid Id { get; init; }
}

/// <summary>Another aggregate root: a many-to-one reference to it is outside the boundary.</summary>
public sealed class RowSharingOwner : IAggregateRoot
{
    public Guid Id { get; init; }
}

public sealed class RowSharingDbContext(DbContextOptions<RowSharingDbContext> options, RowSharing shape) : DbContext(options), IModelVariant
{
    public RowSharing Shape { get; } = shape;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var root = modelBuilder.Entity<RowSharingRoot>();
        root.Ignore(r => r.Owner);
        root.Ignore(r => r.Tags);
        root.Ignore(r => r.Owners);
        switch (Shape)
        {
            case RowSharing.ManyToOneReference:
                root.HasOne(r => r.Contact).WithMany().HasForeignKey(r => r.ContactId);
                break;
            case RowSharing.ManyToOneOutside:
                root.HasOne(r => r.Contact).WithMany().HasForeignKey(r => r.ContactId);
                root.Navigation(r => r.Contact).IsOutsideAggregate();
                break;
            case RowSharing.OneToOne:
                root.HasOne(r => r.Contact).WithOne().HasForeignKey<RowSharingRoot>(r => r.ContactId);
                break;
            case RowSharing.ManyToMany:
                root.Ignore(r => r.Contact);
                modelBuilder.Entity<RowSharingRoot>().HasMany(r => r.Tags).WithMany();
                break;
            case RowSharing.ManyToManyOutside:
                root.Ignore(r => r.Contact);
                modelBuilder.Entity<RowSharingRoot>().HasMany(r => r.Tags).WithMany();
                modelBuilder.Entity<RowSharingRoot>().Navigation(r => r.Tags).IsOutsideAggregate();
                break;
            case RowSharing.ManyToManyToARoot:
                root.Ignore(r => r.Contact);
                modelBuilder.Entity<RowSharingRoot>().HasMany(r => r.Owners).WithMany();
                break;
            case RowSharing.ManyToManyToARootOutside:
                root.Ignore(r => r.Contact);
                modelBuilder.Entity<RowSharingRoot>().HasMany(r => r.Owners).WithMany();
                modelBuilder.Entity<RowSharingRoot>().Navigation(r => r.Owners).IsOutsideAggregate(); // its hidden inverse too
                break;
            case RowSharing.ManyToOneToARoot:
                root.Ignore(r => r.Contact);
                modelBuilder.Entity<RowSharingRoot>().HasOne(r => r.Owner).WithMany().HasForeignKey(r => r.OwnerId);
                break;
        }
    }

    object IModelVariant.Variant => Shape;

    // One model per variant: the shared cache key factory includes it.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => ModelVariantCacheKeyFactory.Use(optionsBuilder);
}
