using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.ValueObjects.Common;
using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.EntityFrameworkCore;

/// <summary>
/// What the conventions promise in their documentation, read off the built model: which
/// properties they convert, which they leave alone, and that explicit configuration wins.
/// </summary>
public sealed class DomainConventionModelTests
{
    [Fact]
    public void TheAuditConvention_ConvertsDiscoveredAuditInstants_AndNothingElse()
    {
        using var context = new DiscoveredMemoContext();
        var memo = context.Model.FindEntityType(typeof(Memo))!;

        ConverterName(memo, nameof(Memo.CreatedAt)).ShouldBe("UtcInstantConverter",
            "a property EF Core discovers by itself gets the convention");
        ConverterName(memo, nameof(Memo.UpdatedAt)).ShouldBe("UtcInstantConverter", "the nullable instant too");
        ConverterName(memo, nameof(Memo.DueAt)).ShouldBeNull(
            "a DateTimeOffset that is not an audit instant keeps its offset");
    }

    [Fact]
    public void AnExplicitConversion_TakesPrecedenceOverTheAuditConvention()
    {
        using var context = new ExplicitMemoContext();
        var memo = context.Model.FindEntityType(typeof(Memo))!;

        ConverterName(memo, nameof(Memo.CreatedAt)).ShouldBe(nameof(DateTimeOffsetToBinaryConverter));
        ConverterName(memo, nameof(Memo.UpdatedAt)).ShouldBe("UtcInstantConverter");
    }

    [Fact]
    public void GuidIds_AreConverted_AsKeysAndAsOptionalProperties()
    {
        using var context = new DiscoveredMemoContext();
        var memo = context.Model.FindEntityType(typeof(Memo))!;

        ConverterName(memo, nameof(Memo.Id)).ShouldBe("GuidIdConverter");
        ConverterName(memo, nameof(Memo.ReplyTo)).ShouldBe("GuidIdConverter", "a GuidId? property");
        memo.FindProperty(nameof(Memo.ReplyTo))!.IsNullable.ShouldBeTrue();
    }

    private static string? ConverterName(IEntityType entityType, string property) =>
        entityType.FindProperty(property)!.GetValueConverter()?.GetType().Name;

    // One context type per variant: EF Core caches the built model per context type.
    private sealed class DiscoveredMemoContext() : MemoContextBase(explicitCreatedAt: false);

    private sealed class ExplicitMemoContext() : MemoContextBase(explicitCreatedAt: true);

    private abstract class MemoContextBase(bool explicitCreatedAt) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseSqlite("DataSource=:memory:");

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.AddDomainConventions();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // No property is configured explicitly unless the test asks for it.
            var memo = modelBuilder.Entity<Memo>();
            memo.Ignore(m => m.DomainEvents);
            if (explicitCreatedAt)
                memo.Property(m => m.CreatedAt).HasConversion(new DateTimeOffsetToBinaryConverter());
        }
    }
}

public sealed class Memo : AuditableAggregateRoot<GuidId>
{
    private Memo(GuidId id) : base(id, DateTimeOffset.UnixEpoch, Actor.Migration) { }

    public DateTimeOffset DueAt { get; private set; }

    public GuidId? ReplyTo { get; private set; }
}
