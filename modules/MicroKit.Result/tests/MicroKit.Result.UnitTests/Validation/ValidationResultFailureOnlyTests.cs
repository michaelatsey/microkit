namespace MicroKit.Result.UnitTests.Validation;

// ADR-RESULT-001 D10: ToResult<T>() returns a failure-only Result<T>, and throws on a valid ValidationResult.
public sealed class ValidationResultFailureOnlyTests
{
    [Fact]
    public void ToResultOfT_WhenOneError_ReturnsFailureWithCollectionOfThatError()
    {
        // Arrange
        var validation = new ValidationResult().AddError("Name", "Name is required.");

        // Act
        Result<Order> result = validation.ToResult<Order>();

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        var errors = result.Error.ShouldBeOfType<ErrorCollection>();
        errors.Count.ShouldBe(1);
        errors[0].ShouldBeSameAs(validation.Errors[0]);
    }

    [Fact]
    public void ToResultOfT_WhenSeveralErrors_ReturnsFailureWithAllErrorsInOrder()
    {
        // Arrange
        var custom = ValidationError.Required("Email");
        var validation = new ValidationResult()
            .AddError("Name", "Name is required.")
            .AddError(custom)
            .AddErrorIf(true, "Age", "Age must be non-negative.")
            .AddErrorIf(false, "Skipped", "Never added.");

        // Act
        Result<Order> result = validation.ToResult<Order>();

        // Assert
        result.IsFailure.ShouldBeTrue();
        var errors = result.Error.ShouldBeOfType<ErrorCollection>();
        errors.Count.ShouldBe(3);
        errors.ShouldBe(validation.Errors.Cast<IError>().ToArray());
        errors[1].ShouldBeSameAs(custom);
        errors.OfType<IValidationError>().Select(e => e.PropertyName).ShouldBe(["Name", "Email", "Age"]);
        errors.Category.ShouldBe(ErrorCategory.Validation);
    }

    [Fact]
    public void ToResultOfT_WhenErrors_MatchesFailureOfToResultWithValue()
    {
        // Arrange
        var validation = new ValidationResult().AddError("Name", "Name is required.").AddError("Age", "Too young.");

        // Act
        Result<Order> failureOnly = validation.ToResult<Order>();
        Result<Order> withValue = validation.ToResult(new Order(1));

        // Assert
        failureOnly.IsFailure.ShouldBeTrue();
        withValue.IsFailure.ShouldBeTrue();
        failureOnly.Error.Code.ShouldBe(withValue.Error.Code);
        failureOnly.Error.Message.ShouldBe(withValue.Error.Message);
        var failureOnlyErrors = failureOnly.Error.ShouldBeOfType<ErrorCollection>();
        var withValueErrors = withValue.Error.ShouldBeOfType<ErrorCollection>();
        failureOnlyErrors.ShouldBe(withValueErrors, ignoreOrder: false);
    }

    [Fact]
    public void ToResultOfT_WhenUsedForEarlyReturn_PropagatesFailure()
    {
        // Act
        Result<Order> result = CreateOrder(quantity: 0);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ErrorCollection>().Count.ShouldBe(1);
    }

    [Fact]
    public void ToResultOfT_WhenValid_ThrowsInvalidOperationException()
    {
        // Arrange
        var validation = new ValidationResult();

        // Act + Assert
        var thrown = Should.Throw<InvalidOperationException>(() => validation.ToResult<Order>());
        thrown.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public void ToResultOfT_WhenOnlyFalseConditionsAdded_ThrowsInvalidOperationException()
    {
        // Arrange
        var validation = new ValidationResult().AddErrorIf(false, "Name", "Name is required.");

        // Act + Assert
        Should.Throw<InvalidOperationException>(() => validation.ToResult<int>());
    }

    private static Result<Order> CreateOrder(int quantity)
    {
        var validation = new ValidationResult().AddErrorIf(quantity <= 0, "Quantity", "Quantity must be positive.");
        if (!validation.IsValid)
            return validation.ToResult<Order>();
        return Result.Success(new Order(quantity));
    }

    private sealed record Order(int Quantity);
}
