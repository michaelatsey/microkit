using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Identifiers;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.Aggregates;

public sealed class EntityTests
{
    [Fact]
    public void Constructor_DefaultStructId_ThrowsArgumentException()
    {
        var ex = Should.Throw<ArgumentException>(() => new StructIdEntity(default));

        ex.ParamName.ShouldBe("id");
    }

    [Fact]
    public void Constructor_EmptyGuidId_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => new StructIdEntity(GuidId.From(Guid.Empty)));
    }

    [Fact]
    public void Constructor_NullClassId_ThrowsArgumentNullException()
    {
        var ex = Should.Throw<ArgumentNullException>(() => new ClassIdEntity(null!));

        ex.ParamName.ShouldBe("id");
    }

    [Fact]
    public void Constructor_NonDefaultStructId_SetsId()
    {
        var id = GuidId.New();

        new StructIdEntity(id).Id.ShouldBe(id);
    }

    [Fact]
    public void Equals_SubclassWithSameId_IsEqualBothWays()
    {
        var id = GuidId.New();
        Entity<GuidId> entity = new StructIdEntity(id);
        Entity<GuidId> proxy = new StructIdEntityProxy(id);

        entity.Equals(proxy).ShouldBeTrue();
        proxy.Equals(entity).ShouldBeTrue();
        entity.Equals((object)proxy).ShouldBeTrue();
        proxy.Equals((object)entity).ShouldBeTrue();
        (entity == proxy).ShouldBeTrue();
        (proxy == entity).ShouldBeTrue();
        entity.GetHashCode().ShouldBe(proxy.GetHashCode());
    }

    [Fact]
    public void Equals_UnrelatedEntityTypesWithSameId_AreNotEqual()
    {
        var id = GuidId.New();
        Entity<GuidId> left = new StructIdEntity(id);
        Entity<GuidId> right = new OtherStructIdEntity(id);

        left.Equals(right).ShouldBeFalse();
        right.Equals(left).ShouldBeFalse();
        (left == right).ShouldBeFalse();
        (left != right).ShouldBeTrue();
    }

    [Fact]
    public void Equals_SameTypeWithDifferentIds_AreNotEqual()
    {
        var left = new StructIdEntity(GuidId.New());
        var right = new StructIdEntity(GuidId.New());

        left.Equals(right).ShouldBeFalse();
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var entity = new StructIdEntity(GuidId.New());

        entity.Equals(null).ShouldBeFalse();
        (entity == null).ShouldBeFalse();
    }

    [Fact]
    public void Equals_ClassIdsWithSameValue_AreEqual()
    {
        var value = Guid.NewGuid();

        new ClassIdEntity(new ClassId(value)).ShouldBe(new ClassIdEntity(new ClassId(value)));
    }
}

internal sealed record ClassId(Guid Value) : IEntityId;

internal class StructIdEntity(GuidId id) : Entity<GuidId>(id);

// Stands in for an EF Core proxy: a runtime subclass of the mapped entity.
internal sealed class StructIdEntityProxy(GuidId id) : StructIdEntity(id);

internal sealed class OtherStructIdEntity(GuidId id) : Entity<GuidId>(id);

internal sealed class ClassIdEntity(ClassId id) : Entity<ClassId>(id);
