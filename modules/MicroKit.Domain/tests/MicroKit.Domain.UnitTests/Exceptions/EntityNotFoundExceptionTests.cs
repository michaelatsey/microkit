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
}

public sealed class MissingOrder;
