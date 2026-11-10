
namespace MicroKit.Persistence.UnitTests.Results;

using MicroKit.Result;

/// <summary>#197 phase 3: the codes and messages of the Result path, and the default error name.</summary>
public sealed class PersistenceErrorsTests
{
    [Theory]
    [InlineData(typeof(Order), "ORDER")]
    [InlineData(typeof(OrderLine), "ORDER_LINE")]
    [InlineData(typeof(HTTPRequest), "HTTP_REQUEST")]
    [InlineData(typeof(Order2), "ORDER2")]
    [InlineData(typeof(V2Order), "V2_ORDER")]
    [InlineData(typeof(Generic<int>), "GENERIC")]
    public void DefaultErrorName_IsTheSimpleClrNameInUpperSnake(Type type, string expected)
    {
        PersistenceErrors.DefaultErrorName(type).ShouldBe(expected);
    }

    [Theory]
    [InlineData("ORDER", true)]
    [InlineData("SHOP_ORDER", true)]
    [InlineData("ORDER2", true)]
    [InlineData("order", false)]
    [InlineData("_ORDER", false)]
    [InlineData("ORDER_", false)]
    [InlineData("SHOP__ORDER", false)]
    [InlineData("2ORDER", false)]
    [InlineData("SHOP.ORDER", false)]
    [InlineData("COMMANDÉ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidErrorName_AcceptsUpperSnakeOnly(string? name, bool valid)
    {
        PersistenceErrors.IsValidErrorName(name).ShouldBe(valid);
    }

    [Fact]
    public void Errors_WithAName_AreCodedWithTheRoot()
    {
        PersistenceErrors.Duplicate("ORDER").Code.Value.ShouldBe("PERSISTENCE.ORDER.DUPLICATE");
        PersistenceErrors.Reference("ORDER").Code.Value.ShouldBe("PERSISTENCE.ORDER.REFERENCE");
        PersistenceErrors.Concurrency("ORDER").Code.Value.ShouldBe("PERSISTENCE.ORDER.CONCURRENCY");
    }

    [Fact]
    public void Errors_WithoutAName_AreCodedWithTheReasonOnly()
    {
        PersistenceErrors.Duplicate().Code.Value.ShouldBe("PERSISTENCE.DUPLICATE");
        PersistenceErrors.Reference().Code.Value.ShouldBe("PERSISTENCE.REFERENCE");
        PersistenceErrors.Concurrency().Code.Value.ShouldBe("PERSISTENCE.CONCURRENCY");
    }

    [Fact]
    public void Errors_AreConflicts_WithAMessageThatNamesNoRoot()
    {
        var error = PersistenceErrors.Concurrency("ORDER");

        error.Category.ShouldBe(ErrorCategory.Conflict);
        error.Message.ShouldNotContain("ORDER", Case.Insensitive);
        error.ShouldBe(PersistenceErrors.Concurrency("ORDER"));
    }

    [Fact]
    public void Errors_WithAnInvalidName_Throw()
    {
        Should.Throw<ArgumentException>(() => PersistenceErrors.Duplicate("order"));
    }

    [Fact]
    public void AggregateVersionConflictException_IsAnInvalidOperationException_CarryingTheName()
    {
        var conflict = new AggregateVersionConflictException("stale") { ErrorName = "ORDER" };

        conflict.ShouldBeAssignableTo<InvalidOperationException>();
        conflict.ErrorName.ShouldBe("ORDER");
        new AggregateVersionConflictException("stale").ErrorName.ShouldBeNull();
        new AggregateVersionConflictException("stale", null).InnerException.ShouldBeNull("the standard (message, inner) constructor");
        Should.Throw<ArgumentException>(() => new AggregateVersionConflictException("stale") { ErrorName = "order" });
    }

    private sealed class Order;

    private sealed class OrderLine;

    private sealed class HTTPRequest;

    private sealed class Order2;

    private sealed class V2Order;

    private sealed class Generic<T>;
}
