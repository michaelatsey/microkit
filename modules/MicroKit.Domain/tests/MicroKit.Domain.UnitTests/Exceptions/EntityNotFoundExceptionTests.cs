using MicroKit.Domain.Exceptions;
using MicroKit.Domain.Identifiers;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.Exceptions;

public sealed class EntityNotFoundExceptionTests
{
    [Fact]
    public void Constructor_CarriesTheIdentifierAndTheEntityType()
    {
        var id = GuidId.From(Guid.CreateVersion7());

        var ex = new EntityNotFoundException<MissingOrder, GuidId>(id);

        ex.EntityId.ShouldBe(id);
        ex.EntityType.ShouldBe(typeof(MissingOrder));
        ex.Message.ShouldBe($"MissingOrder with ID '{id}' was not found.");
        ex.ShouldBeAssignableTo<DomainException>();
    }

    [Fact]
    public void Message_WithADerivedEntityId_NamesTheGuidValue()
    {
        var value = Guid.CreateVersion7();

        new EntityNotFoundException<MissingOrder, MissingOrderId>(new MissingOrderId(value)).Message
            .ShouldBe($"MissingOrder with ID '{value}' was not found.");
    }
}

public sealed class MissingOrder;

public sealed record MissingOrderId : EntityId<MissingOrderId>
{
    public MissingOrderId(Guid value) : base(value) { }
}
