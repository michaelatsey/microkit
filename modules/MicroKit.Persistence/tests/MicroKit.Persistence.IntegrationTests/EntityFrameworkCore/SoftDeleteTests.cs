using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Identifiers;
using MicroKit.Persistence.Abstractions;
using MicroKit.Persistence.EntityFrameworkCore;
using MicroKit.Persistence.IntegrationTests.PostgreSql;
using MicroKit.Persistence.IntegrationTests.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// #197 phase 4: an aggregate whose root has <c>HasSoftDelete()</c> is never deleted: deleting the root
/// marks every entity of the aggregate, and the named filter hides them. Every scenario runs on PostgreSQL
/// and on SQL Server.
/// </summary>
public abstract class SoftDeleteScenarios
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 30, 0, TimeSpan.Zero);

    // One application service provider for every context: EF Core keys its internal providers on it.
    private static readonly IServiceProvider s_applicationServices =
        new ServiceCollection().AddSingleton<TimeProvider>(new FixedTimeProvider(Now)).BuildServiceProvider();

    /// <summary>Configures the provider on <paramref name="database"/>.</summary>
    protected abstract void UseProvider(DbContextOptionsBuilder builder, string database, int? maxRetryCount = null);

    /// <summary>The provider's classifier, for the Result path.</summary>
    protected abstract IDatabaseFailureClassifier Classifier { get; }

    // ---- What a soft delete writes ------------------------------------------------------------------

    [DockerRequiredFact]
    public async Task ASoftDeletedRoot_MarksEveryEntityOfItsAggregate_AndDeletesNoRow()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            var orders = Orders(context);
            await orders.DeleteAsync((await orders.FindAsync(id)).ShouldNotBeNull());
            await Commit(context);
        }

        await using var verify = db.Create();
        var order = await Unfiltered(verify.Set<SdOrder>()).Include(o => o.Notes).SingleAsync(o => o.Id == id);
        Mark(verify, order).ShouldBe(Now);
        Mark(verify, order)!.Value.Offset.ShouldBe(TimeSpan.Zero);
        verify.Entry(order).Property<long>(AggregateVersion.PropertyName).CurrentValue.ShouldBe(2, "a soft delete is a write");
        order.Address.ShouldNotBeNull().City.ShouldBe("Lyon", "a table-split owned type keeps its values");
        order.Notes.ShouldHaveSingleItem().Text.ShouldBe("fragile", "an owned collection in its own table keeps its rows");

        await AllMarkedAsync<SdLine>(verify, expected: 3);
        await AllMarkedAsync<SdOption>(verify, expected: 1);
        await AllMarkedAsync<SdContact>(verify, expected: 1);
        await AllMarkedAsync<SdMemo>(verify, expected: 1);
        var remark = await Unfiltered(verify.Set<SdRemark>()).SingleAsync();
        Mark(verify, remark).ShouldBe(Now);
        remark.OrderId.ShouldBe(id, "a ClientSetNull child keeps its foreign key");
    }

    [DockerRequiredFact]
    public async Task AChildRemovedFromALiveRoot_IsDeletedForReal()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            var order = (await Orders(context).FindAsync(id)).ShouldNotBeNull();
            order.Lines.Remove(order.Lines.First(l => l.Sku == "PEN"));
            await Commit(context);
        }

        await using var verify = db.Create();
        (await Unfiltered(verify.Set<SdLine>()).CountAsync()).ShouldBe(2);
        (await verify.Set<SdLine>().CountAsync()).ShouldBe(2);
    }

    [DockerRequiredFact]
    public async Task AChildRemovedInTheSaveThatSoftDeletesItsRoot_IsMarked_NotDeleted()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            var orders = Orders(context);
            var order = (await orders.FindAsync(id)).ShouldNotBeNull();
            order.Lines.Remove(order.Lines.First(l => l.Sku == "PEN"));
            await orders.DeleteAsync(order);
            await Commit(context);
        }

        await using var verify = db.Create();
        await AllMarkedAsync<SdLine>(verify, expected: 3);
    }

    [DockerRequiredFact]
    public async Task TwoAggregatesInOneSave_OneSoftDeleted_TheOtherLosingAChildForReal()
    {
        await using var db = await CreateDatabaseAsync();
        var orderId = await SeedOrderAsync(db, "SO-1");
        var basketId = await SeedBasketAsync(db);
        await using (var context = db.Create())
        {
            var orders = Orders(context);
            await orders.DeleteAsync((await orders.FindAsync(orderId)).ShouldNotBeNull());
            var basket = (await Baskets(context).FindAsync(basketId)).ShouldNotBeNull();
            basket.Items.RemoveAt(0);
            await Commit(context);
        }

        await using var verify = db.Create();
        await AllMarkedAsync<SdLine>(verify, expected: 3);
        (await verify.Set<SdBasketItem>().CountAsync()).ShouldBe(1, "the live basket lost its item for real");
    }

    [DockerRequiredFact]
    public async Task AnAggregateWithoutSoftDelete_IsDeletedForReal()
    {
        await using var db = await CreateDatabaseAsync();
        var basketId = await SeedBasketAsync(db);
        await using (var context = db.Create())
        {
            var baskets = Baskets(context);
            await baskets.DeleteAsync((await baskets.FindAsync(basketId)).ShouldNotBeNull());
            await Commit(context);
        }

        await using var verify = db.Create();
        (await verify.Set<SdBasket>().CountAsync()).ShouldBe(0);
        (await verify.Set<SdBasketItem>().CountAsync()).ShouldBe(0);
    }

    // ---- Delete behaviours and timings --------------------------------------------------------------

    public enum Timing
    {
        CascadeOnSaveChanges,
        OrphansOnSaveChanges,
    }

    [DockerRequiredTheory]
    [InlineData(Timing.CascadeOnSaveChanges)]
    [InlineData(Timing.OrphansOnSaveChanges)]
    public async Task UnderOnSaveChangesTimings_EveryEntityEndsMarked_AndNoRowIsDeleted(Timing timing)
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            if (timing == Timing.CascadeOnSaveChanges)
                context.ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
            else
                context.ChangeTracker.DeleteOrphansTiming = CascadeTiming.OnSaveChanges;

            var orders = Orders(context);
            var order = (await orders.FindAsync(id)).ShouldNotBeNull();
            if (timing == Timing.OrphansOnSaveChanges)
                order.Lines.Remove(order.Lines.First(l => l.Sku == "PEN")); // a conceptual null on a required key
            await orders.DeleteAsync(order);
            await Commit(context);
        }

        await using var verify = db.Create();
        (await Unfiltered(verify.Set<SdOrder>()).CountAsync(o => o.Id == id)).ShouldBe(1);
        await AllMarkedAsync<SdLine>(verify, expected: 3);
        await AllMarkedAsync<SdOption>(verify, expected: 1);
        await AllMarkedAsync<SdMemo>(verify, expected: 1);
        await AllMarkedAsync<SdRemark>(verify, expected: 1);
    }

    [DockerRequiredFact]
    public async Task ARequiredClientNoActionChild_IsMarked_AndNotDeleted()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            (await Orders(context).FindAsync(id)).ShouldNotBeNull().Stamps.Add(new SdStamp { Text = "paid" });
            await Commit(context);
        }

        await SoftDeleteAsync(db, id);

        await using var verify = db.Create();
        await AllMarkedAsync<SdStamp>(verify, expected: 1);
        (await Unfiltered(verify.Set<SdStamp>()).SingleAsync()).OrderId.ShouldBe(id);
    }

    [DockerRequiredFact]
    public async Task AChildMovedToALiveRoot_FollowsIt_AndIsNotMarked()
    {
        await using var db = await CreateDatabaseAsync();
        var deletedId = await SeedOrderAsync(db, "SO-1");
        var liveId = await SeedOrderAsync(db, "SO-2");
        await using (var context = db.Create())
        {
            var orders = Orders(context);
            var deleted = (await orders.FindAsync(deletedId)).ShouldNotBeNull();
            var live = (await orders.FindAsync(liveId)).ShouldNotBeNull();
            var moved = deleted.Lines.First(l => l.Sku == "PEN");
            deleted.Lines.Remove(moved);
            live.Lines.Add(moved);
            await orders.DeleteAsync(deleted);
            await Commit(context);
        }

        await using var verify = db.Create();
        var survivor = await Orders(verify).FindAsync(liveId);
        survivor.ShouldNotBeNull().Lines.Count.ShouldBe(4, "the moved line is a live line of the other order");
        (await Unfiltered(verify.Set<SdLine>()).CountAsync()).ShouldBe(6);
    }

    [DockerRequiredFact]
    public async Task AChildAddedInTheSaveThatSoftDeletesItsRoot_IsInsertedMarked()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            context.ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges; // Immediate detaches an Added child
            var orders = Orders(context);
            var order = (await orders.FindAsync(id)).ShouldNotBeNull();
            order.Lines.Add(new SdLine { Sku = "LATE" });
            await orders.DeleteAsync(order);
            await Commit(context);
        }

        await using var verify = db.Create();
        await AllMarkedAsync<SdLine>(verify, expected: 4);
    }

    // ---- Reads --------------------------------------------------------------------------------------

    [DockerRequiredFact]
    public async Task ADirectQueryOnAChildSet_ReturnsNoChildOfASoftDeletedRoot()
    {
        await using var db = await CreateDatabaseAsync();
        var deletedId = await SeedOrderAsync(db, "SO-1");
        await SeedOrderAsync(db, "SO-2");
        await SoftDeleteAsync(db, deletedId);

        await using var verify = db.Create();
        (await verify.Set<SdLine>().CountAsync()).ShouldBe(3, "only the live order's lines");
        (await verify.Set<SdOption>().CountAsync()).ShouldBe(1);
    }

    [DockerRequiredFact]
    public async Task TheReadSide_ReturnsSoftDeletedAggregatesOnlyWithDeleted_AndKeepsOtherNamedFilters()
    {
        await using var db = await CreateDatabaseAsync();
        var deletedId = await SeedOrderAsync(db, "SO-1");
        await SeedOrderAsync(db, "SO-2");
        var hiddenId = await SeedOrderAsync(db, "SO-3");
        await SoftDeleteAsync(db, deletedId);
        await SoftDeleteAsync(db, hiddenId);
        await using (var setup = db.Create())
        {
            // Another tenant's soft-deleted order: the write side cannot reach it, so move it there afterwards.
            await setup.Set<SdOrder>().IgnoreQueryFilters([SoftDelete.FilterName])
                .Where(o => o.Id == hiddenId)
                .ExecuteUpdateAsync(u => u.SetProperty(o => o.Tenant, SoftDeleteDbContext.HiddenTenant));
        }

        await using var context = db.Create();
        var reads = new EfReadRepository<SdOrder, SoftDeleteDbContext>(context, new EfSpecificationEvaluator(), PaginationLimits.Default);

        var live = await reads.ListAsync(new QueryOptions<SdOrder>());
        var all = await reads.ListAsync(new QueryOptions<SdOrder>().WithDeleted());

        live.Select(o => o.Number).ShouldBe(["SO-2"]);
        all.Select(o => o.Number).OrderBy(n => n, StringComparer.Ordinal).ToList().ShouldBe(["SO-1", "SO-2"]); // the tenant filter still hides SO-3
        (await reads.CountAsync(new QueryOptions<SdOrder>())).ShouldBe(1);
        (await reads.CountAsync(new QueryOptions<SdOrder>().WithDeleted())).ShouldBe(2);
        (await reads.AnyAsync(new QueryOptions<SdOrder>(new NumberIs("SO-1")))).ShouldBeFalse();
        (await reads.AnyAsync(new QueryOptions<SdOrder>(new NumberIs("SO-1")).WithDeleted())).ShouldBeTrue();
        (await reads.ListPagedAsync(new QueryOptions<SdOrder>().WithDeleted().WithOrdering(q => q.OrderBy(o => o.Number)))).TotalCount.ShouldBe(2);
        (await reads.FindAsync([deletedId])).ShouldBeNull();
    }

    [DockerRequiredFact]
    public async Task FindAsync_AfterTheCommit_ReturnsNull_InTheSameScopeAndInANewOne()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            var orders = Orders(context);
            await orders.DeleteAsync((await orders.FindAsync(id)).ShouldNotBeNull());
            await Commit(context);

            context.ChangeTracker.Entries().ShouldBeEmpty("a soft-deleted aggregate is detached, as a deleted one is");
            (await orders.FindAsync(id)).ShouldBeNull();
        }

        await using var fresh = db.Create();
        (await Orders(fresh).FindAsync(id)).ShouldBeNull();
    }

    [DockerRequiredFact]
    public async Task AFilteredUniqueIndex_AdmitsASoftDeletedDuplicate_AndRejectsTwoLiveOnes()
    {
        await using var db = await CreateDatabaseAsync();
        var first = await SeedOrderAsync(db, "SO-1");
        await SoftDeleteAsync(db, first);

        await SeedOrderAsync(db, "SO-1"); // the soft-deleted one does not hold the number

        await Should.ThrowAsync<PersistenceException>(() => SeedOrderAsync(db, "SO-1"));
    }

    // ---- Refusals and versions ---------------------------------------------------------------------

    [DockerRequiredFact]
    public async Task SoftDeletingARootThatWasNotLoadedWhole_FailsTheSave()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using var context = db.Create();
        var stub = new SdOrder { Id = id, Number = "SO-1", Tenant = "main" };
        context.Attach(stub);
        context.Remove(stub);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        thrown.Message.ShouldContain("not loaded whole");
        await using var verify = db.Create();
        (await verify.Set<SdOrder>().CountAsync()).ShouldBe(1, "nothing was written");
    }

    [DockerRequiredFact]
    public async Task ASoftDeleteAtAStaleVersion_Conflicts()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var other = db.Create())
        {
            (await Orders(other).FindAsync(id))!.Number = "SO-1B";
            await Commit(other);
        }

        await using var context = db.Create();
        var orders = Orders(context);
        await orders.DeleteAsync((await orders.FindAsync(id, expectedVersion: 1)).ShouldNotBeNull());

        var thrown = await Should.ThrowAsync<PersistenceException>(() => Commit(context));

        thrown.InnerException.ShouldBeOfType<DbUpdateConcurrencyException>();
        await using var verify = db.Create();
        (await verify.Set<SdOrder>().CountAsync()).ShouldBe(1, "still live");
    }

    [DockerRequiredTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AChangeMadeBeforeTheDelete_IsWrittenWithTheMark(bool autoDetectChanges)
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            // Without automatic detection, nothing detects again after the step: it flags the changes itself.
            context.ChangeTracker.AutoDetectChangesEnabled = autoDetectChanges;
            var orders = Orders(context);
            var order = (await orders.FindAsync(id)).ShouldNotBeNull();
            order.Number = "SO-1-CANCELLED";
            order.Lines[0].Sku = "BOOK-RETURNED";
            order.Total = new SdMoney(42m); // a complex property
            context.ChangeTracker.DetectChanges();
            await orders.DeleteAsync(order);
            await Commit(context);
        }

        await using var verify = db.Create();
        var stored = await Unfiltered(verify.Set<SdOrder>()).SingleAsync();
        stored.Number.ShouldBe("SO-1-CANCELLED");
        stored.Total.Amount.ShouldBe(42m);
        (await Unfiltered(verify.Set<SdLine>()).CountAsync(l => l.Sku == "BOOK-RETURNED")).ShouldBe(1);
    }

    // ---- Review findings: resolution, outside references, lifecycle -------------------------------

    [DockerRequiredFact]
    public async Task AChildMovedIntoTheRootBeforeItsSoftDeletion_IsMarked_NotDeleted()
    {
        await using var db = await CreateDatabaseAsync();
        var deletedId = await SeedOrderAsync(db, "SO-1");
        var liveId = await SeedOrderAsync(db, "SO-2");
        await using (var context = db.Create())
        {
            var orders = Orders(context);
            var deleted = (await orders.FindAsync(deletedId)).ShouldNotBeNull();
            var live = (await orders.FindAsync(liveId)).ShouldNotBeNull();
            var moved = live.Lines.First(l => l.Sku == "PEN");
            live.Lines.Remove(moved);
            deleted.Lines.Add(moved);
            await orders.DeleteAsync(deleted);
            await Commit(context);
        }

        await using var verify = db.Create();
        (await Unfiltered(verify.Set<SdLine>()).CountAsync()).ShouldBe(6, "no line is deleted");
        (await Orders(verify).FindAsync(liveId)).ShouldNotBeNull().Lines.Count.ShouldBe(2);
    }

    [DockerRequiredFact]
    public async Task APrincipalSideChildTakenByALiveRoot_IsNotMarked()
    {
        await using var db = await CreateDatabaseAsync();
        var deletedId = await SeedOrderAsync(db, "SO-1");
        var liveId = await SeedOrderAsync(db, "SO-2", withContact: false);
        Guid contactId;
        await using (var context = db.Create())
        {
            var orders = Orders(context);
            var deleted = (await orders.FindAsync(deletedId)).ShouldNotBeNull();
            var live = (await orders.FindAsync(liveId)).ShouldNotBeNull();
            var contact = deleted.Contact.ShouldNotBeNull();
            contactId = contact.Id;
            deleted.Contact = null;
            live.Contact = contact;
            await orders.DeleteAsync(deleted);
            await Commit(context);
        }

        await using var verify = db.Create();
        (await Orders(verify).FindAsync(liveId)).ShouldNotBeNull().Contact.ShouldNotBeNull().Id.ShouldBe(contactId);
    }

    [DockerRequiredFact]
    public async Task ATrackedEntityOutsideTheAggregate_KeepsItsReferenceToTheSoftDeletedRoot()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        var ticketId = await SeedTicketAsync(db, id, line: null);
        await using (var context = db.Create())
        {
            _ = await context.Set<SdTicket>().SingleAsync(t => t.Id == ticketId); // tracked
            var orders = Orders(context);
            await orders.DeleteAsync((await orders.FindAsync(id)).ShouldNotBeNull());
            await Commit(context);
        }

        await using var verify = db.Create();
        (await verify.Set<SdTicket>().SingleAsync()).OrderId.ShouldBe(id, "nothing deleted the order: its key stays");
    }

    [DockerRequiredFact]
    public async Task AChildRemovedFromALiveRoot_WhileAnOutsideEntityReferencesIt_ReturnsReference()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        Guid penId;
        await using (var lookup = db.Create())
            penId = (await Orders(lookup).FindAsync(id))!.Lines.Single(l => l.Sku == "PEN").Id;
        await SeedTicketAsync(db, order: null, line: penId);
        await using var context = db.Create();
        var unitOfWork = new EfUnitOfWork<SoftDeleteDbContext>(context);

        var result = await unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                var order = await s.Orders.FindAsync(s.Id, ct);
                order!.Lines.Remove(order.Lines.Single(l => l.Sku == "PEN")); // a real delete: the order stays live
                await s.UnitOfWork.CommitAsync(ct);
                return MicroKit.Result.Result.Success();
            },
            (Orders: Orders(context), UnitOfWork: unitOfWork, Id: id));

        result.Error.Code.Value.ShouldBe("PERSISTENCE.SD_ORDER.REFERENCE");
        await using var verify = db.Create();
        (await verify.Set<SdLine>().CountAsync()).ShouldBe(3);
    }

    [DockerRequiredFact]
    public void KeysIntoASoftDeletableAggregateFromOutside_AreClientNoAction()
    {
        using var context = new SoftDeleteDbContext(Options<SoftDeleteDbContext>("unused"));
        var model = context.Model;

        model.FindEntityType(typeof(SdTicket))!.GetForeignKeys().Select(fk => fk.DeleteBehavior).Distinct()
            .ShouldBe([DeleteBehavior.ClientNoAction]);
        var join = model.GetEntityTypes().Single(t => t.IsPropertyBag);
        join.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(SdOrder)).DeleteBehavior
            .ShouldBe(DeleteBehavior.ClientNoAction, "the join rows of an outside many-to-many");
        model.FindEntityType(typeof(SdLine))!.GetForeignKeys().Single().DeleteBehavior
            .ShouldBe(DeleteBehavior.Cascade, "a key inside the aggregate keeps its behaviour");
    }

    [DockerRequiredTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnOwnedValueReplacedBeforeTheSoftDelete_IsWritten(bool onSaveChanges)
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            if (onSaveChanges)
                context.ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
            var orders = Orders(context);
            var order = (await orders.FindAsync(id)).ShouldNotBeNull();
            order.Address = new SdAddress { City = "Paris" };
            order.Notes.Add(new SdNote { Text = "late" });
            await orders.DeleteAsync(order);
            await Commit(context);
        }

        await using var verify = db.Create();
        var stored = await Unfiltered(verify.Set<SdOrder>()).Include(o => o.Notes).SingleAsync();
        stored.Address.ShouldNotBeNull().City.ShouldBe("Paris");
        stored.Notes.Select(n => n.Text).OrderBy(t => t, StringComparer.Ordinal).ToList().ShouldBe(["fragile", "late"]);
    }

    [DockerRequiredFact]
    public async Task ASoftDeleteRetriedAfterATransientFailure_DetachesOnlyWhatTheSuccessfulAttemptMarked()
    {
        await using var db = await CreateDatabaseAsync();
        var deletedId = await SeedOrderAsync(db, "SO-1");
        var otherId = await SeedOrderAsync(db, "SO-2");
        var failing = new FailingUpdateInterceptor(failures: 1);
        await using var context = db.Create(o => o.AddInterceptors(failing), maxRetryCount: 3);
        var unitOfWork = new EfUnitOfWork<SoftDeleteDbContext>(context);

        await unitOfWork.ExecuteAsync(
            static async (s, ct) =>
            {
                await s.Orders.DeleteAsync((await s.Orders.FindAsync(s.Id, ct)).ShouldNotBeNull(), ct);
                await s.UnitOfWork.CommitAsync(ct);
            },
            (Orders: Orders(context), UnitOfWork: unitOfWork, Id: deletedId));

        failing.Thrown.ShouldBe(1);
        await AssertTheContextStillTracksANewAggregateAsync(context, otherId);
    }

    [DockerRequiredFact]
    public async Task ASoftDeleteSavedWithoutAcceptingItsChanges_IsWrittenAgain_WhenTheSaveIsRetried()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            var orders = Orders(context);
            var order = (await orders.FindAsync(id)).ShouldNotBeNull();
            await orders.DeleteAsync(order);

            // The pattern of a save retried by hand inside a transaction: SaveChanges(false), and on a failure the
            // transaction rolls back and the same changes are saved again.
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                await context.SaveChangesAsync(acceptAllChangesOnSuccess: false);
                await transaction.RollbackAsync();
            }

            context.Entry(order).State.ShouldBe(EntityState.Modified, "the changes were not accepted: they stay tracked");
            await context.SaveChangesAsync();
            context.ChangeTracker.Entries().ShouldBeEmpty("an accepted soft delete is detached");
        }

        await using var verify = db.Create();
        var stored = await Unfiltered(verify.Set<SdOrder>()).Include(o => o.Notes).SingleAsync(o => o.Id == id);
        Mark(verify, stored).ShouldBe(Now);
        stored.Notes.ShouldHaveSingleItem();
        await AllMarkedAsync<SdLine>(verify, expected: 3);
        await AllMarkedAsync<SdOption>(verify, expected: 1);
        await AllMarkedAsync<SdContact>(verify, expected: 1);
        await AllMarkedAsync<SdRemark>(verify, expected: 1);
        await AllMarkedAsync<SdMemo>(verify, expected: 1);
    }

    [DockerRequiredFact]
    public async Task ASoftDeleteSavedWithoutAcceptingItsChanges_ThenAcceptedByHand_IsNotFoundAgain()
    {
        await using var db = await CreateDatabaseAsync();
        var id = await SeedOrderAsync(db, "SO-1");
        await using (var context = db.Create())
        {
            var orders = Orders(context);
            await orders.DeleteAsync((await orders.FindAsync(id)).ShouldNotBeNull());

            // EF Core's resilient pattern: SaveChanges(false), commit, then AcceptAllChanges(), which raises no
            // SavedChanges.
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                await context.SaveChangesAsync(acceptAllChangesOnSuccess: false);
                await transaction.CommitAsync();
            }

            context.ChangeTracker.AcceptAllChanges();

            (await orders.FindAsync(id)).ShouldBeNull("the write side never loads a soft-deleted aggregate again");
            context.ChangeTracker.Entries().ShouldBeEmpty("the accepted soft delete is detached");
        }

        await using var verify = db.Create();
        await AllMarkedAsync<SdLine>(verify, expected: 3);
    }

    [DockerRequiredTheory]
    [InlineData(SdBaseKeyDbContext.Variant.PrincipalIsTheRootsBase, "required foreign key 'SdDocLine.DocId'")]
    [InlineData(SdBaseKeyDbContext.Variant.DeclaredOnAChildsBase, "'SdPartBase.OwnerId'")] // refused as a key from outside
    public void ARequiredKeyThroughAMappedBaseType_FailsModelBuilding(SdBaseKeyDbContext.Variant variant, string reason)
    {
        using var context = new SdBaseKeyDbContext(Options<SdBaseKeyDbContext>("unused", ModelVariantCacheKeyFactory.Use), variant);

        var thrown = Should.Throw<InvalidOperationException>(() => _ = context.Model);

        thrown.Message.ShouldContain(reason);
        thrown.Message.ShouldContain("OnDelete(DeleteBehavior.Restrict)");
    }

    [DockerRequiredTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFailedSoftDelete_ThenAClearedTracker_LeavesNothingForTheNextSave(bool pooled)
    {
        await using var db = await CreateDatabaseAsync();
        var deletedId = await SeedOrderAsync(db, "SO-1");
        var otherId = await SeedOrderAsync(db, "SO-2");
        await using (var other = db.Create())
        {
            (await Orders(other).FindAsync(deletedId))!.Number = "SO-1B";
            await Commit(other);
        }

        var pool = new Microsoft.EntityFrameworkCore.Infrastructure.PooledDbContextFactory<SoftDeleteDbContext>(db.OptionsFor(), poolSize: 1);
        var first = pooled ? pool.CreateDbContext() : db.Create();
        var orders = Orders(first);
        await orders.DeleteAsync((await orders.FindAsync(deletedId, expectedVersion: 1)).ShouldNotBeNull());
        await Should.ThrowAsync<PersistenceException>(() => Commit(first)); // stale: nothing written

        SoftDeleteDbContext next;
        if (pooled)
        {
            await first.DisposeAsync(); // back to the pool: the lease is reset
            next = pool.CreateDbContext();
            next.ShouldBeSameAs(first, "the pool hands the same instance out again");
        }
        else
        {
            new EfUnitOfWork<SoftDeleteDbContext>(first).DiscardChanges();
            next = first;
        }

        await AssertTheContextStillTracksANewAggregateAsync(next, otherId);
        await next.DisposeAsync();
        await using var verify = db.Create();
        (await verify.Set<SdOrder>().CountAsync()).ShouldBe(2, "the failed soft delete wrote nothing");
    }

    [DockerRequiredFact]
    public async Task AnAnonymousFilterOnAnotherAggregate_StillAppliesWithDeleted()
    {
        await using var db = await CreateDatabaseAsync();
        await using (var context = db.Create())
        {
            context.Add(new SdBasket { Id = new SdOrderId(Guid.NewGuid()), Owner = SoftDeleteDbContext.HiddenTenant });
            await Commit(context);
        }

        await using var reads = db.Create();
        var baskets = new EfReadRepository<SdBasket, SoftDeleteDbContext>(reads, new EfSpecificationEvaluator(), PaginationLimits.Default);

        (await baskets.ListAsync(new QueryOptions<SdBasket>().WithDeleted())).ShouldBeEmpty();
        (await baskets.CountAsync(new QueryOptions<SdBasket>().WithDeleted())).ShouldBe(0);
    }

    [DockerRequiredFact]
    public void ARootWithAMappedBaseType_CarriesTheFilterOnTheBaseType()
    {
        using var context = new SdBaseTypeDbContext(Options<SdBaseTypeDbContext>("unused"));

        var record = context.Model.FindEntityType(typeof(SdRecord))!;
        record.FindDeclaredQueryFilter(SoftDelete.FilterName).ShouldNotBeNull();
        record.FindDeclaredProperty(SoftDelete.PropertyName).ShouldNotBeNull();
    }

    [DockerRequiredFact]
    public async Task ARootAndAChildSharingATable_ShareOneMarkColumn()
    {
        var name = "softdelete_split_" + Guid.NewGuid().ToString("N");
        await using (var setup = new SdSplitDbContext(Options<SdSplitDbContext>(name)))
            await setup.Database.EnsureCreatedAsync();
        try
        {
            var id = new SdFolderId(Guid.NewGuid());
            await using (var context = new SdSplitDbContext(Options<SdSplitDbContext>(name)))
            {
                var folder = new SdFolder { Id = id, Name = "invoices", Details = new SdFolderDetails { Description = "2026" } };
                await new EfRepository<SdFolder, SdFolderId, SdSplitDbContext>(context).AddAsync(folder);
                await new EfUnitOfWork<SdSplitDbContext>(context).CommitAsync();
            }

            await using (var context = new SdSplitDbContext(Options<SdSplitDbContext>(name)))
            {
                var folders = new EfRepository<SdFolder, SdFolderId, SdSplitDbContext>(context);
                await folders.DeleteAsync((await folders.FindAsync(id)).ShouldNotBeNull());
                await new EfUnitOfWork<SdSplitDbContext>(context).CommitAsync();
            }

            await using var verify = new SdSplitDbContext(Options<SdSplitDbContext>(name));
            var stored = await verify.Set<SdFolder>().IgnoreQueryFilters([SoftDelete.FilterName]).Include(f => f.Details).SingleAsync();
            Mark(verify, stored).ShouldBe(Now);
            Mark(verify, stored.Details).ShouldBe(Now, "one column, one mark");
            stored.Details.Description.ShouldBe("2026");
        }
        finally
        {
            await using var cleanup = new SdSplitDbContext(Options<SdSplitDbContext>(name));
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    // ---- The model ---------------------------------------------------------------------------------

    [DockerRequiredFact]
    public void TheModel_PutsTheMarkAndTheFilterOnEachHierarchyRoot_NotOnOwnedTypes()
    {
        using var context = new SoftDeleteDbContext(Options<SoftDeleteDbContext>("unused"));
        var model = context.Model;

        foreach (var type in new[] { typeof(SdOrder), typeof(SdLine), typeof(SdOption), typeof(SdContact), typeof(SdRemark), typeof(SdMemo), typeof(SdStamp) })
        {
            var entity = model.FindEntityType(type.FullName!)!;
            entity.FindDeclaredProperty(SoftDelete.PropertyName).ShouldNotBeNull(type.Name).ClrType.ShouldBe(typeof(DateTimeOffset?));
            entity.FindDeclaredQueryFilter(SoftDelete.FilterName).ShouldNotBeNull(type.Name);
        }

        model.FindEntityType(typeof(SdGiftLine))!.FindDeclaredProperty(SoftDelete.PropertyName).ShouldBeNull("a derived type inherits it");
        model.FindEntityType(typeof(SdAddress))!.FindProperty(SoftDelete.PropertyName).ShouldBeNull("an owned type follows its owner");
        model.FindEntityType(typeof(SdNote))!.FindProperty(SoftDelete.PropertyName).ShouldBeNull();
        model.FindEntityType(typeof(SdBasket))!.FindProperty(SoftDelete.PropertyName).ShouldBeNull("not soft-deletable");
        model.FindEntityType(typeof(SdOrder))!.FindDeclaredQueryFilter("Tenant").ShouldNotBeNull("another named filter is kept");
    }

    public enum Misuse
    {
        OnAChild,
        OnADerivedRoot,
        AnonymousFilter,
        RequiredRestrict,
        RequiredClientSetNull,
        RequiredWithoutNavigation,
        ClrMember,
        NotDateTimeOffset,
        SameColumn,
        SameColumnUnderTpc,
        UnrelatedTpcColumn,
        MarkRequired,
        MarkDefaultValueSql,
        MarkValueGenerated,
        MarkConverted,
        OutsideCascade,
        OutsideRestrict,
    }

    [DockerRequiredTheory]
    [InlineData(Misuse.OnAChild, "is not the top-most aggregate root")]
    [InlineData(Misuse.OnADerivedRoot, "is not the top-most aggregate root")]
    [InlineData(Misuse.AnonymousFilter, "carries an anonymous query filter")]
    [InlineData(Misuse.RequiredRestrict, "OnDelete(DeleteBehavior.Restrict)")]
    [InlineData(Misuse.RequiredClientSetNull, "OnDelete(DeleteBehavior.ClientSetNull)")]
    [InlineData(Misuse.RequiredWithoutNavigation, "required foreign key 'SdGiftLine.SourceLineId' (no navigation) is configured with OnDelete(DeleteBehavior.Restrict)")]
    [InlineData(Misuse.ClrMember, "maps it to a CLR member")]
    [InlineData(Misuse.NotDateTimeOffset, "not 'DateTimeOffset?'")]
    [InlineData(Misuse.SameColumn, "is also mapped by")]
    [InlineData(Misuse.SameColumnUnderTpc, "reserved on 'SdTpcDocument', an entity of the soft-deletable aggregate 'SdTpcDocument'")]
    [InlineData(Misuse.MarkRequired, "configures it as required")]
    [InlineData(Misuse.MarkDefaultValueSql, "gives it a default value")]
    [InlineData(Misuse.MarkValueGenerated, "configures it as generated")]
    [InlineData(Misuse.MarkConverted, "gives it a value converter")]
    [InlineData(Misuse.OutsideCascade, "OnDelete(DeleteBehavior.Cascade)")]
    [InlineData(Misuse.OutsideRestrict, "OnDelete(DeleteBehavior.Restrict)")]
    public void AMisconfiguredSoftDelete_FailsModelBuilding(Misuse misuse, string reason)
    {
        using var context = new SoftDeleteMisuseDbContext(Options<SoftDeleteMisuseDbContext>("unused", ModelVariantCacheKeyFactory.Use), misuse);

        var thrown = Should.Throw<InvalidOperationException>(() => _ = context.Model);

        thrown.Message.ShouldContain(reason);
    }

    [DockerRequiredFact]
    public void AMarkColumnInTheTableOfAnUnrelatedTpcType_IsNotACollision()
    {
        using var context = new SoftDeleteMisuseDbContext(
            Options<SoftDeleteMisuseDbContext>("unused", ModelVariantCacheKeyFactory.Use), Misuse.UnrelatedTpcColumn);

        Should.NotThrow(() => _ = context.Model);
    }

    // ---- Helpers -----------------------------------------------------------------------------------

    private static IQueryable<T> Unfiltered<T>(IQueryable<T> query) where T : class =>
        query.IgnoreQueryFilters([SoftDelete.FilterName]);

    private static DateTimeOffset? Mark(DbContext context, object entity) =>
        (DateTimeOffset?)context.Entry(entity).Property(SoftDelete.PropertyName).CurrentValue;

    private static async Task AllMarkedAsync<T>(SoftDeleteDbContext context, int expected) where T : class
    {
        var rows = await Unfiltered(context.Set<T>()).ToListAsync();
        rows.Count.ShouldBe(expected, $"no {typeof(T).Name} row is gone");
        foreach (var row in rows)
            Mark(context, row).ShouldBe(Now, typeof(T).Name);
    }

    private static EfRepository<SdOrder, SdOrderId, SoftDeleteDbContext> Orders(SoftDeleteDbContext context) => new(context);

    private static EfRepository<SdBasket, SdOrderId, SoftDeleteDbContext> Baskets(SoftDeleteDbContext context) => new(context);

    private static Task Commit(SoftDeleteDbContext context) => new EfUnitOfWork<SoftDeleteDbContext>(context).CommitAsync().AsTask();

    private static async Task SoftDeleteAsync(Database db, SdOrderId id)
    {
        await using var context = db.Create();
        var orders = Orders(context);
        await orders.DeleteAsync((await orders.FindAsync(id)).ShouldNotBeNull());
        await Commit(context);
    }

    private static async Task AssertTheContextStillTracksANewAggregateAsync(SoftDeleteDbContext context, SdOrderId id)
    {
        context.ChangeTracker.Entries().ShouldBeEmpty();
        var order = (await Orders(context).FindAsync(id)).ShouldNotBeNull();
        order.Number = "SO-2-RENAMED";
        await Commit(context);
        context.Entry(order).State.ShouldBe(EntityState.Unchanged, "the next save detaches nothing it did not soft-delete");
        context.ChangeTracker.Entries().Count().ShouldBeGreaterThan(1);
    }

    private static async Task<Guid> SeedTicketAsync(Database db, SdOrderId? order, Guid? line)
    {
        var ticket = new SdTicket { OrderId = order, LineId = line };
        await using var context = db.Create();
        context.Add(ticket);
        await Commit(context);
        return ticket.Id;
    }

    private static async Task<SdOrderId> SeedOrderAsync(Database db, string number, bool withContact = true)
    {
        var order = new SdOrder
        {
            Id = new SdOrderId(Guid.NewGuid()),
            Number = number,
            Address = new SdAddress { City = "Lyon" },
            Contact = withContact ? new SdContact { Name = "Ada" } : null,
        };
        var book = new SdLine { Sku = "BOOK" };
        book.Options.Add(new SdOption { Name = "gift wrap" });
        order.Lines.Add(book);
        order.Lines.Add(new SdLine { Sku = "PEN" });
        order.Lines.Add(new SdGiftLine { Sku = "CARD", Message = "Happy birthday" });
        order.Notes.Add(new SdNote { Text = "fragile" });
        order.Remarks.Add(new SdRemark { Text = "call first" });
        order.Memos.Add(new SdMemo { Text = "VIP" });

        await using var context = db.Create();
        await Orders(context).AddAsync(order);
        await Commit(context);
        return order.Id;
    }

    private static async Task<SdOrderId> SeedBasketAsync(Database db)
    {
        var basket = new SdBasket { Id = new SdOrderId(Guid.NewGuid()) };
        basket.Items.Add(new SdBasketItem { Sku = "APPLE" });
        basket.Items.Add(new SdBasketItem { Sku = "PEAR" });
        await using var context = db.Create();
        await Baskets(context).AddAsync(basket);
        await Commit(context);
        return basket.Id;
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
        builder.UseApplicationServiceProvider(s_applicationServices).UseMicroKitPersistence(Classifier);
        return builder.Options;
    }

    private async Task<Database> CreateDatabaseAsync()
    {
        var db = new Database(this, "softdelete_" + Guid.NewGuid().ToString("N"));
        await using var context = db.Create();
        await context.Database.EnsureCreatedAsync();
        return db;
    }

    /// <summary>One database of the provider, dropped on dispose.</summary>
    private sealed class Database(SoftDeleteScenarios owner, string name) : IAsyncDisposable
    {
        public string Name { get; } = name;

        public SoftDeleteDbContext Create(Action<DbContextOptionsBuilder>? configure = null, int? maxRetryCount = null) =>
            new(owner.Options<SoftDeleteDbContext>(Name, configure, maxRetryCount));

        public DbContextOptions<SoftDeleteDbContext> OptionsFor() => owner.Options<SoftDeleteDbContext>(Name);

        public async ValueTask DisposeAsync()
        {
            await using var context = Create();
            await context.Database.EnsureDeletedAsync();
        }
    }

    /// <summary>Fails the first <c>failures</c> UPDATE commands with the exception a test classifies as transient.</summary>
    private sealed class FailingUpdateInterceptor(int failures) : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        private int _remaining = failures;

        public int Thrown { get; private set; }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (_remaining > 0 && command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                _remaining--;
                Thrown++;
                throw new TransientMarkerException();
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NumberIs(string number) : MicroKit.Domain.Specifications.Specification<SdOrder>
    {
        public override System.Linq.Expressions.Expression<Func<SdOrder, bool>> ToExpression() => o => o.Number == number;
    }
}

[Collection(PostgreSqlSuite.Name)]
public sealed class SoftDeletePostgreSqlTests(PostgreSqlSuiteFixture fixture) : SoftDeleteScenarios
{
    protected override IDatabaseFailureClassifier Classifier => MicroKit.Persistence.EntityFrameworkCore.PostgreSql.PostgreSqlFailureClassifier.Instance;

    protected override void UseProvider(DbContextOptionsBuilder builder, string database, int? maxRetryCount = null) =>
        builder.UseNpgsql(fixture.GetConnectionString(database), npgsql =>
        {
            if (maxRetryCount is { } retries)
                npgsql.ExecutionStrategy(d => new MarkerRetryingExecutionStrategy(d, retries));
        });
}

[Collection(SqlServerSuite.Name)]
public sealed class SoftDeleteSqlServerTests(SqlServerSuiteFixture fixture) : SoftDeleteScenarios
{
    protected override IDatabaseFailureClassifier Classifier => MicroKit.Persistence.EntityFrameworkCore.SqlServer.SqlServerFailureClassifier.Instance;

    protected override void UseProvider(DbContextOptionsBuilder builder, string database, int? maxRetryCount = null) =>
        builder.UseSqlServer(fixture.GetConnectionString(database), sql =>
        {
            if (maxRetryCount is { } retries)
                sql.ExecutionStrategy(d => new MarkerRetryingExecutionStrategy(d, retries));
        });
}

public readonly record struct SdOrderId(Guid Value) : IEntityId;

/// <summary>
/// A soft-deletable aggregate: a collection with a grandchild and a derived type, a contact the root
/// references (one-to-one), an owned type in the root's table and an owned collection in its own, an
/// optional ClientSetNull child and an optional ClientNoAction one.
/// </summary>
public class SdOrder : IAggregateRoot
{
    public SdOrderId Id { get; init; }

    public string Number { get; set; } = string.Empty;

    public string Tenant { get; set; } = "main";

    public List<SdLine> Lines { get; } = [];

    public SdAddress? Address { get; set; }

    public List<SdNote> Notes { get; } = [];

    public Guid? ContactId { get; set; }

    public SdContact? Contact { get; set; }

    public List<SdRemark> Remarks { get; } = [];

    public List<SdMemo> Memos { get; } = [];

    public List<SdStamp> Stamps { get; } = [];

    public SdMoney Total { get; set; } = new(0m);

    public List<SdLabel> Labels { get; } = [];
}

/// <summary>A value object mapped as a complex property.</summary>
public sealed record SdMoney(decimal Amount);

/// <summary>Reference data outside every aggregate, linked to orders by a many-to-many marked outside.</summary>
public sealed class SdLabel
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
}

/// <summary>An entity outside every aggregate that references an order and one of its lines.</summary>
public sealed class SdTicket
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public SdOrderId? OrderId { get; set; }

    public Guid? LineId { get; set; }
}

public sealed class SdPriorityOrder : SdOrder;

public class SdLine
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Sku { get; set; } = string.Empty;

    public List<SdOption> Options { get; } = [];
}

public sealed class SdGiftLine : SdLine
{
    public string Message { get; set; } = string.Empty;
}

/// <summary>A line type that maps the reserved name to a CLR member.</summary>
public sealed class SdMarkedLine : SdLine
{
    public DateTimeOffset? AggregateDeletedAt { get; set; }
}

public sealed class SdOption
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
}

public sealed class SdAddress
{
    public string City { get; set; } = string.Empty;
}

public sealed class SdNote
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Text { get; set; } = string.Empty;
}

public sealed class SdContact
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
}

public sealed class SdRemark
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public SdOrderId? OrderId { get; set; }

    public string Text { get; set; } = string.Empty;
}

public sealed class SdMemo
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public SdOrderId? OrderId { get; set; }

    public string Text { get; set; } = string.Empty;
}

/// <summary>A required child whose key is ClientNoAction: EF Core leaves it alone when its root is removed.</summary>
public sealed class SdStamp
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public SdOrderId OrderId { get; set; }

    public string Text { get; set; } = string.Empty;
}

/// <summary>A soft-deletable TPC root whose abstract base has no table.</summary>
public abstract class SdTpcDocument : IAggregateRoot
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

public sealed class SdTpcInvoice : SdTpcDocument
{
    public DateTimeOffset? Legacy { get; set; }
}

/// <summary>Outside every aggregate: a TPC hierarchy whose concrete row has its own AggregateDeletedAt column.</summary>
public abstract class SdTpcLogBase
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

public sealed class SdTpcLog : SdTpcLogBase
{
    public DateTimeOffset? AggregateDeletedAt { get; set; }
}

/// <summary>An aggregate without soft delete.</summary>
public sealed class SdBasket : IAggregateRoot
{
    public SdOrderId Id { get; init; }

    public string Owner { get; set; } = "me";

    public List<SdBasketItem> Items { get; } = [];
}

public sealed class SdBasketItem
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Sku { get; set; } = string.Empty;
}

public class SoftDeleteDbContext(DbContextOptions options) : DbContext(options)
{
    public const string HiddenTenant = "hidden";

    private static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<SdOrderId, Guid> OrderIdConverter =
        new(id => id.Value, v => new SdOrderId(v));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SdOrder>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).HasConversion(id => id.Value, v => new SdOrderId(v)).ValueGeneratedNever();
            b.HasSoftDelete();
            b.HasQueryFilter("Tenant", o => o.Tenant != HiddenTenant);
            // The README recipe: a soft-deleted order does not hold its number.
            b.HasIndex(o => o.Number).IsUnique().HasFilter("\"AggregateDeletedAt\" IS NULL");
            b.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId").IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.OwnsOne(o => o.Address);
            b.OwnsMany(o => o.Notes, n =>
            {
                n.ToTable("SdNotes");
                n.HasKey(x => x.Id);
                n.Property(x => x.Id).ValueGeneratedNever();
            });
            b.HasOne(o => o.Contact).WithOne().HasForeignKey<SdOrder>(o => o.ContactId);
            b.HasMany(o => o.Remarks).WithOne().HasForeignKey(r => r.OrderId).IsRequired(false).OnDelete(DeleteBehavior.ClientSetNull);
            b.HasMany(o => o.Memos).WithOne().HasForeignKey(m => m.OrderId).IsRequired(false).OnDelete(DeleteBehavior.ClientNoAction);
            b.HasMany(o => o.Stamps).WithOne().HasForeignKey(s => s.OrderId).IsRequired().OnDelete(DeleteBehavior.ClientNoAction);
            b.ComplexProperty(o => o.Total);
            b.HasMany(o => o.Labels).WithMany();
            b.Navigation(o => o.Labels).IsOutsideAggregate();
        });
        modelBuilder.Entity<SdLabel>().Property(l => l.Id).ValueGeneratedNever();
        modelBuilder.Entity<SdTicket>(b =>
        {
            b.Property(t => t.Id).ValueGeneratedNever();
            b.Property(t => t.OrderId).HasConversion(OrderIdConverter);
            b.HasOne<SdOrder>().WithMany().HasForeignKey(t => t.OrderId).IsRequired(false);
            b.HasOne<SdLine>().WithMany().HasForeignKey(t => t.LineId).IsRequired(false);
        });
        modelBuilder.Entity<SdLine>(b =>
        {
            b.Property(l => l.Id).ValueGeneratedNever();
            b.HasMany(l => l.Options).WithOne().HasForeignKey("LineId").OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<SdGiftLine>();
        modelBuilder.Entity<SdOption>().Property(o => o.Id).ValueGeneratedNever();
        modelBuilder.Entity<SdContact>().Property(c => c.Id).ValueGeneratedNever();
        modelBuilder.Entity<SdRemark>(b =>
        {
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.OrderId).HasConversion(OrderIdConverter);
        });
        modelBuilder.Entity<SdMemo>(b =>
        {
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.OrderId).HasConversion(OrderIdConverter);
        });
        modelBuilder.Entity<SdStamp>(b =>
        {
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.OrderId).HasConversion(OrderIdConverter);
        });
        modelBuilder.Entity<SdBasket>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasQueryFilter(x => x.Owner != HiddenTenant); // anonymous: the basket is not soft-deletable
            b.Property(x => x.Id).HasConversion(id => id.Value, v => new SdOrderId(v)).ValueGeneratedNever();
            b.HasMany(x => x.Items).WithOne().HasForeignKey("BasketId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<SdBasketItem>().Property(i => i.Id).ValueGeneratedNever();
    }
}

public sealed class SoftDeleteMisuseDbContext(DbContextOptions options, SoftDeleteScenarios.Misuse misuse)
    : DbContext(options), IModelVariant
{
    public object Variant => misuse;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SdOrder>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).HasConversion(id => id.Value, v => new SdOrderId(v));
            b.Ignore(o => o.Address).Ignore(o => o.Notes).Ignore(o => o.Contact).Ignore(o => o.Remarks).Ignore(o => o.Memos)
                .Ignore(o => o.Stamps).Ignore(o => o.Total).Ignore(o => o.Labels);
            if (misuse == SoftDeleteScenarios.Misuse.MarkRequired)
                b.Property<DateTimeOffset?>(SoftDelete.PropertyName).IsRequired();
            if (misuse == SoftDeleteScenarios.Misuse.MarkDefaultValueSql)
                b.Property<DateTimeOffset?>(SoftDelete.PropertyName).HasDefaultValueSql("CURRENT_TIMESTAMP");
            if (misuse == SoftDeleteScenarios.Misuse.MarkValueGenerated)
                b.Property<DateTimeOffset?>(SoftDelete.PropertyName).ValueGeneratedOnAddOrUpdate();
            if (misuse == SoftDeleteScenarios.Misuse.MarkConverted)
                b.Property<DateTimeOffset?>(SoftDelete.PropertyName).HasConversion<string>();
            if (misuse != SoftDeleteScenarios.Misuse.OnADerivedRoot)
                b.HasSoftDelete();
            if (misuse == SoftDeleteScenarios.Misuse.AnonymousFilter)
                b.HasQueryFilter(o => o.Tenant != "hidden");
            if (misuse == SoftDeleteScenarios.Misuse.NotDateTimeOffset)
                b.Property<bool>(SoftDelete.PropertyName);
            if (misuse == SoftDeleteScenarios.Misuse.SameColumn)
                b.Property(o => o.Tenant).HasColumnName(SoftDelete.PropertyName);

            var lines = b.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId").IsRequired();
            if (misuse == SoftDeleteScenarios.Misuse.RequiredRestrict)
                lines.OnDelete(DeleteBehavior.Restrict);
            else if (misuse == SoftDeleteScenarios.Misuse.RequiredClientSetNull)
                lines.OnDelete(DeleteBehavior.ClientSetNull);
        });
        modelBuilder.Entity<SdLine>(b =>
        {
            b.Ignore(l => l.Options);
            if (misuse == SoftDeleteScenarios.Misuse.OnAChild)
                b.HasSoftDelete();
        });
        modelBuilder.Entity<SdGiftLine>(b =>
        {
            // A required key between two types of the aggregate that no navigation of the boundary walk reaches.
            if (misuse == SoftDeleteScenarios.Misuse.RequiredWithoutNavigation)
                b.HasOne<SdLine>().WithMany().HasForeignKey("SourceLineId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        });
        if (misuse == SoftDeleteScenarios.Misuse.UnrelatedTpcColumn)
        {
            modelBuilder.Entity<SdTpcLogBase>().UseTpcMappingStrategy();
            modelBuilder.Entity<SdTpcLog>();
        }

        if (misuse == SoftDeleteScenarios.Misuse.SameColumnUnderTpc)
        {
            modelBuilder.Entity<SdTpcDocument>(b =>
            {
                b.UseTpcMappingStrategy();
                b.HasSoftDelete();
            });
            modelBuilder.Entity<SdTpcInvoice>().Property(i => i.Legacy).HasColumnName(SoftDelete.PropertyName);
        }

        if (misuse is SoftDeleteScenarios.Misuse.OutsideCascade or SoftDeleteScenarios.Misuse.OutsideRestrict)
        {
            modelBuilder.Entity<SdTicket>(b =>
            {
                b.Property(t => t.OrderId).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<SdOrderId, Guid>(id => id.Value, v => new SdOrderId(v)));
                b.Ignore(t => t.LineId);
                b.HasOne<SdOrder>().WithMany().HasForeignKey(t => t.OrderId).IsRequired(false)
                    .OnDelete(misuse == SoftDeleteScenarios.Misuse.OutsideCascade ? DeleteBehavior.Cascade : DeleteBehavior.Restrict);
            });
        }

        if (misuse == SoftDeleteScenarios.Misuse.ClrMember)
            modelBuilder.Entity<SdMarkedLine>();
        if (misuse == SoftDeleteScenarios.Misuse.OnADerivedRoot)
            modelBuilder.Entity<SdPriorityOrder>().HasSoftDelete();
    }
}

/// <summary>A mapped base type that is not an aggregate root, under a soft-deletable root (TPH).</summary>
public abstract class SdRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

public sealed class SdArchive : SdRecord, IAggregateRoot
{
    public string Title { get; set; } = string.Empty;
}

public sealed class SdJournal : SdRecord
{
    public string Text { get; set; } = string.Empty;
}

public sealed class SdBaseTypeDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SdRecord>().Property(r => r.Id).ValueGeneratedNever();
        modelBuilder.Entity<SdArchive>().HasSoftDelete();
        modelBuilder.Entity<SdJournal>();
    }
}

/// <summary>A soft-deletable root whose mapped base type, no aggregate root, holds the collection of lines.</summary>
public abstract class SdDocBase
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public List<SdDocLine> Lines { get; } = [];
}

public sealed class SdDoc : SdDocBase, IAggregateRoot;

public sealed class SdDocLine
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid DocId { get; set; }
}

/// <summary>A soft-deletable root whose child type derives from a mapped base that declares a key to the root.</summary>
public sealed class SdBinder : IAggregateRoot
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public List<SdPart> Parts { get; } = [];
}

public abstract class SdPartBase
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid OwnerId { get; set; }
}

public sealed class SdPart : SdPartBase;

public sealed class SdBaseKeyDbContext(DbContextOptions options, SdBaseKeyDbContext.Variant variant) : DbContext(options), IModelVariant
{
    public enum Variant
    {
        PrincipalIsTheRootsBase,
        DeclaredOnAChildsBase,
    }

    object IModelVariant.Variant => variant;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (variant == Variant.PrincipalIsTheRootsBase)
        {
            modelBuilder.Entity<SdDocBase>()
                .HasMany(d => d.Lines).WithOne().HasForeignKey(l => l.DocId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<SdDoc>().HasSoftDelete();
        }
        else
        {
            modelBuilder.Entity<SdBinder>(b =>
            {
                b.HasSoftDelete();
                b.HasMany(x => x.Parts).WithOne().HasForeignKey("BinderId").IsRequired().OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<SdPartBase>()
                .HasOne<SdBinder>().WithMany().HasForeignKey(p => p.OwnerId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<SdPart>();
        }
    }
}

public readonly record struct SdFolderId(Guid Value) : IEntityId;

/// <summary>A root and a one-to-one child of its aggregate that share the root's table.</summary>
public sealed class SdFolder : IAggregateRoot
{
    public SdFolderId Id { get; init; }

    public string Name { get; set; } = string.Empty;

    public SdFolderDetails Details { get; set; } = new();
}

public sealed class SdFolderDetails
{
    public SdFolderId Id { get; init; }

    public string Description { get; set; } = string.Empty;
}

public sealed class SdSplitDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SdFolder>(b =>
        {
            b.ToTable("SdFolders");
            b.Property(f => f.Id).HasConversion(id => id.Value, v => new SdFolderId(v)).ValueGeneratedNever();
            b.HasSoftDelete();
            b.HasOne(f => f.Details).WithOne().HasForeignKey<SdFolderDetails>(d => d.Id);
            b.Navigation(f => f.Details).IsRequired();
        });
        modelBuilder.Entity<SdFolderDetails>(b =>
        {
            b.ToTable("SdFolders");
            b.Property(d => d.Id).HasConversion(id => id.Value, v => new SdFolderId(v));
        });
    }
}
