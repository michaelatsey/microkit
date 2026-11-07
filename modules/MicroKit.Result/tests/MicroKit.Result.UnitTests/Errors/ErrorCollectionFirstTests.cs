using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Errors;

// ErrorCollection.FirstOrDefault is a non-nullable IError: a collection is never empty.
public sealed class ErrorCollectionFirstTests
{
    [Fact]
    public void FirstOrDefault_WhenSeveralErrors_ReturnsFirstError()
    {
        // Arrange
        var first = new TestError("first");
        var collection = ErrorCollection.From(first, new OtherError(), new NotFoundTestError(1));

        // Act
        IError firstError = collection.FirstOrDefault;

        // Assert
        firstError.ShouldBeSameAs(first);
        firstError.ShouldBeSameAs(collection[0]);
    }

    [Fact]
    public void FirstOrDefault_WhenSingleError_ReturnsThatError()
    {
        // Arrange
        var only = new OtherError();
        var collection = ErrorCollection.From(only);

        // Act
        IError firstError = collection.FirstOrDefault;

        // Assert
        firstError.ShouldBeSameAs(only);
    }

    [Fact]
    public void FirstOrDefault_WhenBuiltFromEnumerable_ReturnsFirstInEnumerationOrder()
    {
        // Arrange
        IEnumerable<IError> errors = [new NotFoundTestError(2), new TestError("second")];
        var collection = ErrorCollection.From(errors);

        // Act
        IError firstError = collection.FirstOrDefault;

        // Assert — usable without a null check: its code is read directly, with no '!'
        firstError.ShouldBeOfType<NotFoundTestError>().Id.ShouldBe(2);
        collection.FirstOrDefault.Code.Value.ShouldBe("TEST.NOT_FOUND");
    }

    [Fact]
    public void FirstOrDefault_StaticType_IsNonNullableIError()
    {
        // Arrange
        var property = typeof(ErrorCollection).GetProperty(nameof(ErrorCollection.FirstOrDefault))!;
        var nullability = new System.Reflection.NullabilityInfoContext().Create(property);

        // Act
        var readState = nullability.ReadState;

        // Assert
        property.PropertyType.ShouldBe(typeof(IError));
        readState.ShouldBe(System.Reflection.NullabilityState.NotNull);
    }
}
