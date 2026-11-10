using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// #197 phase 4, step 0: the EF Core 10 facts the soft-delete step relies on, pinned without MicroKit.
/// A <c>SaveChanges</c> interceptor runs before EF Core cascades a delete or rejects a conceptual null; a
/// conceptual null can be cleared through the public API; a deleted owned entry set back to
/// <c>Unchanged</c> keeps its values.
/// </summary>
public sealed class SoftDeleteFoundationTests
{
    public enum Setup
    {
        RequiredRestrict,
        RequiredClientSetNull,
        OptionalClientNoAction,
        OptionalClientSetNull,
        CascadeOnSaveChanges,
        CascadeImmediate,
    }

    [Theory]
    [InlineData(Setup.OptionalClientNoAction, EntityState.Unchanged)]
    [InlineData(Setup.OptionalClientSetNull, EntityState.Modified)]
    [InlineData(Setup.CascadeOnSaveChanges, EntityState.Unchanged)]
    [InlineData(Setup.CascadeImmediate, EntityState.Deleted)]
    public void ASaveChangesInterceptor_SeesTheChildBeforeEfCoreCascades(Setup setup, EntityState child)
    {
        var probe = new StateProbe();
        using var connection = Open();
        var rootId = Seed(connection, setup);
        using var context = new FoundationDbContext(Options(connection, probe), setup);
        if (setup == Setup.CascadeOnSaveChanges)
            context.ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
        var root = context.Roots.Include(r => r.Children).Single(r => r.Id == rootId);

        context.Remove(root);
        Should.Throw<OperationCanceledException>(() => context.SaveChanges());

        probe.Root.ShouldBe(EntityState.Deleted);
        probe.Child.ShouldBe(child);
    }

    [Theory]
    [InlineData(Setup.RequiredRestrict)]
    [InlineData(Setup.RequiredClientSetNull)]
    public void RemovingTheRoot_OfARequiredChildThatDoesNotCascade_ThrowsAtOnce_UnderImmediateTiming(Setup setup)
    {
        using var connection = Open();
        var rootId = Seed(connection, setup);
        using var context = new FoundationDbContext(Options(connection, probe: null), setup);
        var root = context.Roots.Include(r => r.Children).Single(r => r.Id == rootId);

        // Before any SaveChanges: why HasSoftDelete() refuses such a relationship at model building.
        Should.Throw<InvalidOperationException>(() => context.Remove(root)).Message.ShouldContain("has been severed");
    }

    [Theory]
    [InlineData(false, EntityState.Deleted)]
    [InlineData(true, EntityState.Modified)]
    public void AConceptualNull_IsClearedByWritingTheOriginalValueBack(bool restore, EntityState afterCascade)
    {
        using var connection = Open();
        var rootId = Seed(connection, Setup.CascadeImmediate);
        using var context = new FoundationDbContext(Options(connection, probe: null), Setup.CascadeImmediate);
        context.ChangeTracker.DeleteOrphansTiming = CascadeTiming.OnSaveChanges;
        var root = context.Roots.Include(r => r.Children).Single(r => r.Id == rootId);
        var child = context.Entry(root.Children.Single());
        var foreignKey = child.Property(c => c.RootId);
        var original = foreignKey.OriginalValue;

        root.Children.Clear(); // severs a required relationship: a conceptual null, kept behind the original value
        context.ChangeTracker.DetectChanges();
        foreignKey.CurrentValue.ShouldBe(original, "a conceptual null still reads the original value");

        if (restore)
            foreignKey.CurrentValue = original;
        context.ChangeTracker.CascadeChanges(); // what SaveChanges runs after the interceptors

        child.State.ShouldBe(afterCascade, restore ? "the conceptual null is gone: no orphan to delete" : "the orphan is deleted");
    }

    [Fact]
    public void ADeletedOwnedEntry_SetBackToUnchanged_KeepsItsValues()
    {
        using var connection = Open();
        var rootId = Seed(connection, Setup.CascadeImmediate);
        using var context = new FoundationDbContext(Options(connection, probe: null), Setup.CascadeImmediate);
        var root = context.Roots.Include(r => r.Notes).Single(r => r.Id == rootId);

        context.Remove(root);
        var address = context.Entry(root).Reference(r => r.Address).TargetEntry!;
        var note = context.Entry(root.Notes.Single());
        address.State.ShouldBe(EntityState.Deleted);
        note.State.ShouldBe(EntityState.Deleted);

        context.Entry(root).State = EntityState.Unchanged;
        address.State = EntityState.Unchanged;
        note.State = EntityState.Unchanged;
        context.SaveChanges();

        using var verify = new FoundationDbContext(Options(connection, probe: null), Setup.CascadeImmediate);
        var stored = verify.Roots.Include(r => r.Notes).Single(r => r.Id == rootId);
        stored.Address!.City.ShouldBe("Lyon");
        stored.Notes.ShouldHaveSingleItem().Text.ShouldBe("kept");
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static SqliteConnection Open()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static DbContextOptions<FoundationDbContext> Options(SqliteConnection connection, StateProbe? probe)
    {
        var builder = new DbContextOptionsBuilder<FoundationDbContext>().UseSqlite(connection);
        ModelVariantCacheKeyFactory.Use(builder);
        if (probe is not null)
            builder.AddInterceptors(probe);
        return builder.Options;
    }

    private static Guid Seed(SqliteConnection connection, Setup setup)
    {
        using var context = new FoundationDbContext(Options(connection, probe: null), setup);
        context.Database.EnsureCreated();
        var root = new FoundationRoot { Id = Guid.NewGuid(), Address = new FoundationAddress { City = "Lyon" } };
        root.Children.Add(new FoundationChild { Id = Guid.NewGuid() });
        root.Notes.Add(new FoundationNote { Text = "kept" });
        context.Add(root);
        context.SaveChanges();
        return root.Id;
    }

    /// <summary>Records the states a <c>SavingChanges</c> interceptor sees, then cancels the save.</summary>
    private sealed class StateProbe : SaveChangesInterceptor
    {
        public EntityState? Root { get; private set; }

        public EntityState? Child { get; private set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            var tracker = eventData.Context!.ChangeTracker;
            Root = tracker.Entries<FoundationRoot>().Single().State;
            Child = tracker.Entries<FoundationChild>().Single().State;
            throw new OperationCanceledException("probe: nothing is written");
        }
    }
}

public sealed class FoundationRoot
{
    public Guid Id { get; init; }

    public FoundationAddress? Address { get; set; }

    public List<FoundationChild> Children { get; } = [];

    public List<FoundationNote> Notes { get; } = [];
}

public sealed class FoundationAddress
{
    public string City { get; set; } = string.Empty;
}

public sealed class FoundationNote
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Text { get; set; } = string.Empty;
}

public sealed class FoundationChild
{
    public Guid Id { get; init; }

    public Guid RootId { get; set; }

    public Guid? OptionalRootId { get; set; }
}

public sealed class FoundationDbContext(DbContextOptions<FoundationDbContext> options, SoftDeleteFoundationTests.Setup setup)
    : DbContext(options), IModelVariant
{
    public DbSet<FoundationRoot> Roots => Set<FoundationRoot>();

    public object Variant => setup;

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<FoundationRoot>(b =>
        {
            b.OwnsOne(r => r.Address);
            b.OwnsMany(r => r.Notes, n =>
            {
                n.ToTable("FoundationNotes");
                n.HasKey(x => x.Id);
                n.Property(x => x.Id).ValueGeneratedNever();
            });
            var children = b.HasMany(r => r.Children).WithOne();
            switch (setup)
            {
                case SoftDeleteFoundationTests.Setup.RequiredRestrict:
                    children.HasForeignKey(c => c.RootId).IsRequired().OnDelete(DeleteBehavior.Restrict);
                    break;
                case SoftDeleteFoundationTests.Setup.RequiredClientSetNull:
                    children.HasForeignKey(c => c.RootId).IsRequired().OnDelete(DeleteBehavior.ClientSetNull);
                    break;
                case SoftDeleteFoundationTests.Setup.OptionalClientNoAction:
                    children.HasForeignKey(c => c.OptionalRootId).IsRequired(false).OnDelete(DeleteBehavior.ClientNoAction);
                    break;
                case SoftDeleteFoundationTests.Setup.OptionalClientSetNull:
                    children.HasForeignKey(c => c.OptionalRootId).IsRequired(false).OnDelete(DeleteBehavior.ClientSetNull);
                    break;
                default:
                    children.HasForeignKey(c => c.RootId).IsRequired().OnDelete(DeleteBehavior.Cascade);
                    break;
            }
        });
}
