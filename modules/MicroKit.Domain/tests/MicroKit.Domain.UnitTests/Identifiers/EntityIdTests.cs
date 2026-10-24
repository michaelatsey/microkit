using MicroKit.Domain.Identifiers;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.Identifiers;

public sealed class EntityIdTests
{
    [Fact]
    public void Constructor_EmptyGuid_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => new CustomerId(Guid.Empty)).ParamName.ShouldBe("value");
    }

    [Fact]
    public void Constructor_ValidGuid_KeepsTheValue()
    {
        var value = Guid.CreateVersion7();

        var id = new CustomerId(value);

        id.Value.ShouldBe(value);
    }

    [Fact]
    public void ToString_OfADerivedIdentifier_IsTheGuidValue()
    {
        // A derived record would otherwise synthesize "CustomerId { Value = … }".
        var value = Guid.CreateVersion7();

        new CustomerId(value).ToString().ShouldBe(value.ToString());
    }

    [Fact]
    public void Equality_SameOrOtherIdentifierType_IsByValueAndType()
    {
        var value = Guid.CreateVersion7();

        new CustomerId(value).ShouldBe(new CustomerId(value));
        new CustomerId(value).ShouldNotBe(new CustomerId(Guid.CreateVersion7()));
        new CustomerId(value).Equals(new SupplierId(value)).ShouldBeFalse();
    }
}

public sealed record CustomerId : EntityId<CustomerId>
{
    public CustomerId(Guid value) : base(value) { }
}

public sealed record SupplierId : EntityId<SupplierId>
{
    public SupplierId(Guid value) : base(value) { }
}
