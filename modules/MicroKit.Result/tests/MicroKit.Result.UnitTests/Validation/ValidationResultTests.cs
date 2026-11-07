namespace MicroKit.Result.UnitTests.Validation;

public sealed class ValidationResultTests
{
    [Fact]
    public void Constructor_WhenNew_IsValidWithNoErrors()
    {
        // Arrange + Act
        var validation = new ValidationResult();

        // Assert
        validation.IsValid.ShouldBeTrue();
        validation.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void AddError_WhenPropertyAndMessageGiven_AddsValidationError()
    {
        // Arrange
        var validation = new ValidationResult();

        // Act
        var returned = validation.AddError("Name", "Name is required.");

        // Assert
        returned.ShouldBeSameAs(validation);
        validation.IsValid.ShouldBeFalse();
        validation.Errors.Count.ShouldBe(1);
        var error = validation.Errors[0].ShouldBeOfType<ValidationError>();
        error.PropertyName.ShouldBe("Name");
        error.Message.ShouldBe("Name is required.");
    }

    [Fact]
    public void AddError_WhenErrorGiven_AddsSameInstance()
    {
        // Arrange
        var validation = new ValidationResult();
        var error = ValidationError.InvalidEmail();

        // Act
        var returned = validation.AddError(error);

        // Assert
        returned.ShouldBeSameAs(validation);
        validation.IsValid.ShouldBeFalse();
        validation.Errors.Count.ShouldBe(1);
        validation.Errors[0].ShouldBeSameAs(error);
    }

    [Fact]
    public void AddError_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var validation = new ValidationResult();
        IValidationError? error = null;

        // Act
        var exception = Should.Throw<ArgumentNullException>(() => validation.AddError(error!));

        // Assert
        exception.ParamName.ShouldBe("error");
        validation.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void AddError_WhenPropertyNameIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var validation = new ValidationResult();
        string? propertyName = null;

        // Act + Assert
        Should.Throw<ArgumentNullException>(() => validation.AddError(propertyName!, "message"));
        validation.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void AddErrorIf_WhenConditionTrue_AddsError()
    {
        // Arrange
        var validation = new ValidationResult();

        // Act
        var returned = validation.AddErrorIf(true, "Age", "Age must be non-negative.");

        // Assert
        returned.ShouldBeSameAs(validation);
        validation.IsValid.ShouldBeFalse();
        validation.Errors.Count.ShouldBe(1);
        validation.Errors[0].PropertyName.ShouldBe("Age");
        validation.Errors[0].Message.ShouldBe("Age must be non-negative.");
    }

    [Fact]
    public void AddErrorIf_WhenConditionFalse_AddsNothing()
    {
        // Arrange
        var validation = new ValidationResult();

        // Act
        var returned = validation.AddErrorIf(false, "Age", "Age must be non-negative.");

        // Assert
        returned.ShouldBeSameAs(validation);
        validation.IsValid.ShouldBeTrue();
        validation.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void ToResult_WhenValid_ReturnsSuccess()
    {
        // Arrange
        var validation = new ValidationResult().AddErrorIf(false, "Name", "Name is required.");

        // Act
        var result = validation.ToResult();

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
    }

    [Fact]
    public void ToResult_WhenInvalid_ReturnsFailureWithAllErrorsInOrder()
    {
        // Arrange
        var email = ValidationError.InvalidEmail();
        var validation = new ValidationResult()
            .AddError("Name", "Name is required.")
            .AddError(email)
            .AddErrorIf(true, "Age", "Age must be non-negative.");

        // Act
        var result = validation.ToResult();

        // Assert
        result.IsFailure.ShouldBeTrue();
        var errors = result.Error.ShouldBeOfType<ErrorCollection>();
        errors.Count.ShouldBe(3);
        errors[0].ShouldBeSameAs(validation.Errors[0]);
        errors[1].ShouldBeSameAs(email);
        errors[2].ShouldBeSameAs(validation.Errors[2]);
        errors.Category.ShouldBe(ErrorCategory.Validation);
    }

    [Fact]
    public void ToResultOfT_WhenValid_ReturnsSuccessWithValue()
    {
        // Arrange
        var validation = new ValidationResult();
        var value = new Registration("ada@example.com");

        // Act
        var result = validation.ToResult(value);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(value);
    }

    [Fact]
    public void ToResultOfT_WhenInvalid_ReturnsFailureWithAllErrorsInOrder()
    {
        // Arrange
        var validation = new ValidationResult()
            .AddError("Name", "Name is required.")
            .AddError("Email", "Email is required.");

        // Act
        var result = validation.ToResult(new Registration("ada@example.com"));

        // Assert
        result.IsFailure.ShouldBeTrue();
        var errors = result.Error.ShouldBeOfType<ErrorCollection>();
        errors.Count.ShouldBe(2);
        errors[0].ShouldBeSameAs(validation.Errors[0]);
        errors[1].ShouldBeSameAs(validation.Errors[1]);
    }

    [Fact]
    public void ToResult_WhenCalledTwice_ReturnsIndependentCollections()
    {
        // Arrange
        var validation = new ValidationResult().AddError("Name", "Name is required.");
        var first = validation.ToResult();

        // Act
        validation.AddError("Email", "Email is required.");
        var second = validation.ToResult();

        // Assert
        first.IsFailure.ShouldBeTrue();
        second.IsFailure.ShouldBeTrue();
        first.Error.ShouldBeOfType<ErrorCollection>().Count.ShouldBe(1);
        second.Error.ShouldBeOfType<ErrorCollection>().Count.ShouldBe(2);
    }

    private sealed record Registration(string Email);
}
