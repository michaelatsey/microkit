using System.Globalization;

namespace MicroKit.Result.UnitTests.Validation;

public sealed class ValidationErrorTests
{
    [Theory]
    [InlineData("Email", "VALIDATION.EMAIL")]
    [InlineData("firstName", "VALIDATION.FIRSTNAME")]
    [InlineData("Address.Street", "VALIDATION.ADDRESS.STREET")]
    public void Constructor_WhenPropertyNameGiven_BuildsUppercaseValidationCode(string propertyName, string expectedCode)
    {
        // Arrange
        const string message = "invalid";

        // Act
        var error = new ValidationError(propertyName, message);

        // Assert
        error.Code.Value.ShouldBe(expectedCode);
        error.PropertyName.ShouldBe(propertyName);
        error.Message.ShouldBe(message);
    }

    [Fact]
    public void Category_WhenRead_IsValidation()
    {
        // Arrange
        var error = new ValidationError("Email", "invalid");

        // Act
        var category = error.Category;

        // Assert
        category.ShouldBe(ErrorCategory.Validation);
    }

    [Fact]
    public void Severity_WhenRead_IsWarning()
    {
        // Arrange
        var error = new ValidationError("Email", "invalid");

        // Act
        var severity = error.Severity;

        // Assert
        severity.ShouldBe(ErrorSeverity.Warning);
    }

    [Fact]
    public void Metadata_WhenRead_IsEmpty()
    {
        // Arrange
        var error = new ValidationError("Email", "invalid");

        // Act
        var metadata = error.Metadata;

        // Assert
        metadata.ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_WhenPropertyNameIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        string? propertyName = null;

        // Act
        var exception = Should.Throw<ArgumentNullException>(() => new ValidationError(propertyName!, "invalid"));

        // Assert
        exception.ParamName.ShouldBe("propertyName");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhenPropertyNameIsEmptyOrWhitespace_ThrowsArgumentException(string propertyName)
    {
        // Arrange + Act
        var exception = Should.Throw<ArgumentException>(() => new ValidationError(propertyName, "invalid"));

        // Assert
        exception.ShouldBeOfType<ArgumentException>();
        exception.ParamName.ShouldBe("propertyName");
    }

    [Fact]
    public void Required_WhenCalled_BuildsRequiredMessage()
    {
        // Arrange + Act
        var error = ValidationError.Required("Name");

        // Assert
        error.PropertyName.ShouldBe("Name");
        error.Code.Value.ShouldBe("VALIDATION.NAME");
        error.Message.ShouldBe("Name is required.");
    }

    [Fact]
    public void StringLength_WhenCalled_BuildsBetweenMessage()
    {
        // Arrange + Act
        var error = ValidationError.StringLength("Name", 2, 50);

        // Assert
        error.PropertyName.ShouldBe("Name");
        error.Message.ShouldBe("Name must be between 2 and 50 characters.");
    }

    [Fact]
    public void MinLength_WhenCalled_BuildsAtLeastMessage()
    {
        // Arrange + Act
        var error = ValidationError.MinLength("Password", 8);

        // Assert
        error.PropertyName.ShouldBe("Password");
        error.Message.ShouldBe("Password must be at least 8 characters.");
    }

    [Fact]
    public void MaxLength_WhenCalled_BuildsNotExceedMessage()
    {
        // Arrange + Act
        var error = ValidationError.MaxLength("Bio", 280);

        // Assert
        error.PropertyName.ShouldBe("Bio");
        error.Message.ShouldBe("Bio must not exceed 280 characters.");
    }

    [Fact]
    public void OutOfRange_WhenMinAndMax_BuildsBetweenMessage()
    {
        // Arrange + Act
        var error = ValidationError.OutOfRange("Age", 18, 120);

        // Assert
        error.PropertyName.ShouldBe("Age");
        error.Message.ShouldBe("Age must be between 18 and 120.");
    }

    [Fact]
    public void OutOfRange_WhenOnlyMin_BuildsGreaterThanOrEqualMessage()
    {
        // Arrange + Act
        var error = ValidationError.OutOfRange("Age", min: 18);

        // Assert
        error.Message.ShouldBe("Age must be greater than or equal to 18.");
    }

    [Fact]
    public void OutOfRange_WhenOnlyMax_BuildsLessThanOrEqualMessage()
    {
        // Arrange + Act
        var error = ValidationError.OutOfRange("Age", max: 120);

        // Assert
        error.Message.ShouldBe("Age must be less than or equal to 120.");
    }

    [Fact]
    public void OutOfRange_WhenNoBounds_BuildsOutOfRangeMessage()
    {
        // Arrange + Act
        var error = ValidationError.OutOfRange("Age");

        // Assert
        error.Message.ShouldBe("Age is out of range.");
    }

    [Fact]
    public void OutOfRange_WhenMinAndMaxUnderFrenchCulture_FormatsBoundsWithInvariantCulture()
    {
        // Arrange
        const decimal min = 1.5m;
        const decimal max = 2.5m;

        // Act
        var error = CreateInCulture("fr-FR", () => ValidationError.OutOfRange("Price", min, max));

        // Assert
        error.Message.ShouldBe("Price must be between 1.5 and 2.5.");
    }

    [Fact]
    public void OutOfRange_WhenOnlyMinUnderFrenchCulture_FormatsBoundWithInvariantCulture()
    {
        // Arrange
        const double min = 1.5;

        // Act
        var error = CreateInCulture("fr-FR", () => ValidationError.OutOfRange("Price", min: min));

        // Assert
        error.Message.ShouldBe("Price must be greater than or equal to 1.5.");
    }

    [Fact]
    public void OutOfRange_WhenOnlyMaxUnderFrenchCulture_FormatsBoundWithInvariantCulture()
    {
        // Arrange
        const double max = 2.5;

        // Act
        var error = CreateInCulture("fr-FR", () => ValidationError.OutOfRange("Price", max: max));

        // Assert
        error.Message.ShouldBe("Price must be less than or equal to 2.5.");
    }

    [Fact]
    public void InvalidFormat_WhenNoHint_BuildsInvalidFormatMessage()
    {
        // Arrange + Act
        var error = ValidationError.InvalidFormat("Phone");

        // Assert
        error.PropertyName.ShouldBe("Phone");
        error.Message.ShouldBe("Phone has an invalid format.");
    }

    [Fact]
    public void InvalidFormat_WhenHintGiven_AppendsExpectedHint()
    {
        // Arrange + Act
        var error = ValidationError.InvalidFormat("Phone", "E.164");

        // Assert
        error.Message.ShouldBe("Phone has an invalid format. Expected: E.164");
    }

    [Fact]
    public void InvalidEmail_WhenNoPropertyName_UsesEmail()
    {
        // Arrange + Act
        var error = ValidationError.InvalidEmail();

        // Assert
        error.PropertyName.ShouldBe("Email");
        error.Code.Value.ShouldBe("VALIDATION.EMAIL");
        error.Message.ShouldBe("Email is not a valid email address.");
    }

    [Fact]
    public void InvalidEmail_WhenPropertyNameGiven_UsesIt()
    {
        // Arrange + Act
        var error = ValidationError.InvalidEmail("ContactEmail");

        // Assert
        error.PropertyName.ShouldBe("ContactEmail");
        error.Message.ShouldBe("ContactEmail is not a valid email address.");
    }

    [Fact]
    public void InvalidUrl_WhenNoPropertyName_UsesUrl()
    {
        // Arrange + Act
        var error = ValidationError.InvalidUrl();

        // Assert
        error.PropertyName.ShouldBe("Url");
        error.Code.Value.ShouldBe("VALIDATION.URL");
        error.Message.ShouldBe("Url is not a valid URL.");
    }

    [Fact]
    public void InvalidUrl_WhenPropertyNameGiven_UsesIt()
    {
        // Arrange + Act
        var error = ValidationError.InvalidUrl("Website");

        // Assert
        error.PropertyName.ShouldBe("Website");
        error.Message.ShouldBe("Website is not a valid URL.");
    }

    [Fact]
    public void Custom_WhenCalled_UsesMessageAsIs()
    {
        // Arrange + Act
        var error = ValidationError.Custom("Coupon", "Coupon has expired.");

        // Assert
        error.PropertyName.ShouldBe("Coupon");
        error.Code.Value.ShouldBe("VALIDATION.COUPON");
        error.Message.ShouldBe("Coupon has expired.");
    }

    [Fact]
    public void Factory_WhenPropertyNameIsWhitespace_ThrowsArgumentException()
    {
        // Arrange
        const string propertyName = " ";

        // Act + Assert
        Should.Throw<ArgumentException>(() => ValidationError.Required(propertyName));
    }

    [Fact]
    public void ToString_WhenCalled_IncludesPropertyNameAndMessage()
    {
        // Arrange
        var error = ValidationError.Required("Name");

        // Act
        var text = error.ToString();

        // Assert
        text.ShouldBe("ValidationError[Name]: Name is required.");
    }

    [Fact]
    public void ValidationError_WhenCreated_ImplementsIValidationError()
    {
        // Arrange
        var error = ValidationError.Required("Name");

        // Act
        var asValidation = error.ShouldBeAssignableTo<IValidationError>();

        // Assert
        asValidation.ShouldNotBeNull();
        asValidation.PropertyName.ShouldBe("Name");
        asValidation.Category.ShouldBe(ErrorCategory.Validation);
    }

    [Fact]
    public void Equals_WhenSamePropertyAndMessage_ReturnsTrue()
    {
        // Arrange
        var first = ValidationError.Required("Name");
        var second = new ValidationError("Name", "Name is required.");

        // Act
        var equal = first.Equals(second);

        // Assert
        equal.ShouldBeTrue();
    }

    [Fact]
    public void Equals_WhenDifferentProperty_ReturnsFalse()
    {
        // Arrange
        var first = ValidationError.Required("Name");
        var second = ValidationError.Required("Email");

        // Act
        var equal = first.Equals(second);

        // Assert
        equal.ShouldBeFalse();
    }

    [Fact]
    public void With_WhenPropertyNameChanged_RebuildsCodeFromNewPropertyName()
    {
        // Arrange
        var original = new ValidationError("Email", "m");

        // Act
        var changed = original with { PropertyName = "Phone" };

        // Assert
        changed.PropertyName.ShouldBe("Phone");
        changed.Code.ShouldBe(ErrorCode.From("VALIDATION.PHONE"));
    }

    [Fact]
    public void With_WhenPropertyNameChanged_EqualsErrorBuiltWithNewPropertyName()
    {
        // Arrange
        var original = new ValidationError("Email", "m");
        var expected = new ValidationError("Phone", "m");

        // Act
        var changed = original with { PropertyName = "Phone" };

        // Assert
        changed.ShouldBe(expected);
        (changed == expected).ShouldBeTrue();
        changed.GetHashCode().ShouldBe(expected.GetHashCode());
    }

    [Fact]
    public void With_WhenPropertyNameChanged_LeavesOriginalUnchanged()
    {
        // Arrange
        var original = new ValidationError("Email", "m");

        // Act
        var changed = original with { PropertyName = "Phone" };

        // Assert
        changed.ShouldNotBeSameAs(original);
        original.PropertyName.ShouldBe("Email");
        original.Code.ShouldBe(ErrorCode.From("VALIDATION.EMAIL"));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public void With_WhenPropertyNameIsBlank_ThrowsArgumentException(string propertyName)
    {
        // Arrange
        var original = new ValidationError("Email", "m");

        // Act + Assert
        Should.Throw<ArgumentException>(() => original with { PropertyName = propertyName });
    }

    [Fact]
    public void With_WhenPropertyNameIsNull_ThrowsArgumentException()
    {
        // Arrange
        var original = new ValidationError("Email", "m");

        // Act + Assert
        Should.Throw<ArgumentException>(() => original with { PropertyName = null! });
    }

    private static ValidationError CreateInCulture(string cultureName, Func<ValidationError> create)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            return create();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void With_WhenCodeChangedToAnotherValue_ThrowsArgumentException()
    {
        // Arrange
        var error = new ValidationError("Email", "Email is required.");

        // Act + Assert
        Should.Throw<ArgumentException>(() => error with { Code = ErrorCode.From("OTHER.CODE") });
    }

    [Fact]
    public void With_WhenCodeChangedThroughTheErrorType_ThrowsArgumentException()
    {
        // Arrange: the with expression binds to Error.Code
        Error error = new ValidationError("Email", "Email is required.");

        // Act + Assert
        Should.Throw<ArgumentException>(() => error with { Code = ErrorCode.From("OTHER.CODE") });
    }

    [Fact]
    public void With_WhenCodeSetToTheCodeOfItsPropertyName_KeepsTheError()
    {
        // Arrange
        var error = new ValidationError("Email", "Email is required.");

        // Act
        var same = error with { Code = ErrorCode.From("VALIDATION.EMAIL") };

        // Assert
        same.ShouldBe(error);
    }

    [Fact]
    public void With_WhenCodeIsSetAfterItsNewPropertyName_KeepsBoth()
    {
        // Arrange
        var error = new ValidationError("Email", "Email is required.");

        // Act
        var renamed = error with { PropertyName = "Name", Code = ErrorCode.From("VALIDATION.NAME") };

        // Assert
        renamed.Code.ShouldBe(ErrorCode.From("VALIDATION.NAME"));
    }

    [Fact]
    public void With_WhenCodeIsSetBeforeItsNewPropertyName_ThrowsArgumentException()
    {
        // Arrange: members of a with expression are set in the order they are written; set PropertyName alone
        var error = new ValidationError("Email", "Email is required.");

        // Act + Assert
        Should.Throw<ArgumentException>(() => error with { Code = ErrorCode.From("VALIDATION.NAME"), PropertyName = "Name" });
    }
}
