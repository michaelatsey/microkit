using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Errors;

public sealed class ErrorExtensionsTests
{
    [Theory]
    [InlineData(ErrorCategory.NotFound, true)]
    [InlineData(ErrorCategory.Technical, false)]
    public void IsCategory_WhenCalled_ComparesCategory(ErrorCategory category, bool expected)
    {
        // Arrange
        IError error = new NotFoundTestError(1);

        // Act
        var matches = error.IsCategory(category);

        // Assert
        matches.ShouldBe(expected);
    }

    [Theory]
    [InlineData(ErrorSeverity.Critical, true, false, false)]
    [InlineData(ErrorSeverity.Error, false, false, false)]
    [InlineData(ErrorSeverity.Warning, false, true, false)]
    [InlineData(ErrorSeverity.Information, false, false, true)]
    public void SeverityPredicates_WhenCalled_MatchOnlyTheirSeverity(
        ErrorSeverity severity,
        bool critical,
        bool warning,
        bool information)
    {
        // Arrange
        IError error = new SeverityError(severity);

        // Act
        var isCritical = error.IsCritical();
        var isWarning = error.IsWarning();
        var isInformation = error.IsInformation();

        // Assert
        isCritical.ShouldBe(critical);
        isWarning.ShouldBe(warning);
        isInformation.ShouldBe(information);
    }

    [Fact]
    public void IsWarning_WhenValidationError_ReturnsTrue()
    {
        // Arrange
        IError error = ValidationError.Required("Name");

        // Act
        var isWarning = error.IsWarning();

        // Assert
        isWarning.ShouldBeTrue();
    }

    [Theory]
    [InlineData("retryAfter", true)]
    [InlineData("missing", false)]
    [InlineData("RETRYAFTER", false)]
    public void HasMetadata_WhenCalled_ReportsWhetherKeyExists(string key, bool expected)
    {
        // Arrange
        IError error = new CriticalTestError();

        // Act
        var has = error.HasMetadata(key);

        // Assert
        has.ShouldBe(expected);
    }

    [Fact]
    public void TryGetMetadata_WhenKeyPresentWithMatchingType_ReturnsTypedValue()
    {
        // Arrange
        IError error = new CriticalTestError();

        // Act
        var retryAfter = error.TryGetMetadata<int>("retryAfter");

        // Assert
        retryAfter.ShouldBe(30);
    }

    [Fact]
    public void TryGetMetadata_WhenKeyPresentWithReferenceTypeMismatch_ReturnsNull()
    {
        // Arrange
        IError error = new CriticalTestError();

        // Act
        var retryAfter = error.TryGetMetadata<string>("retryAfter");

        // Assert
        retryAfter.ShouldBeNull();
    }

    [Fact]
    public void TryGetMetadata_WhenKeyPresentWithValueTypeMismatch_ReturnsDefault()
    {
        // Arrange
        IError error = new CriticalTestError();

        // Act
        var retryAfter = error.TryGetMetadata<long>("retryAfter");

        // Assert
        retryAfter.ShouldBe(0L);
    }

    [Fact]
    public void TryGetMetadata_WhenKeyAbsent_ReturnsDefault()
    {
        // Arrange
        IError error = new CriticalTestError();

        // Act
        var missingNumber = error.TryGetMetadata<int>("missing");
        var missingText = error.TryGetMetadata<string>("missing");

        // Assert
        missingNumber.ShouldBe(0);
        missingText.ShouldBeNull();
    }

    [Fact]
    public void TryGetMetadata_WhenKeyPresentWithNullValue_ReturnsDefault()
    {
        // Arrange
        IError error = new MetadataError(ErrorMetadata.Create("correlationId", null));

        // Act
        var value = error.TryGetMetadata<string>("correlationId");

        // Assert
        value.ShouldBeNull();
    }

    [Fact]
    public void ToException_WhenCalled_ReturnsResultExceptionWithCodeAndMessage()
    {
        // Arrange
        IError error = new CriticalTestError();

        // Act
        var exception = error.ToException();

        // Assert
        exception.ShouldBeOfType<ResultException>();
        exception.Message.ShouldBe("[TEST.CRITICAL] Critical error");
        exception.Errors.Count.ShouldBe(1);
        exception.Errors[0].ShouldBeSameAs(error);
    }

    [Fact]
    public void IsCategory_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError? error = null;

        // Act
        var exception = Should.Throw<ArgumentNullException>(() => error!.IsCategory(ErrorCategory.NotFound));

        // Assert
        exception.ParamName.ShouldBe("error");
    }

    [Fact]
    public void IsCritical_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError? error = null;

        // Act
        var exception = Should.Throw<ArgumentNullException>(() => error!.IsCritical());

        // Assert
        exception.ParamName.ShouldBe("error");
    }

    [Fact]
    public void IsWarning_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError? error = null;

        // Act
        var exception = Should.Throw<ArgumentNullException>(() => error!.IsWarning());

        // Assert
        exception.ParamName.ShouldBe("error");
    }

    [Fact]
    public void IsInformation_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError? error = null;

        // Act
        var exception = Should.Throw<ArgumentNullException>(() => error!.IsInformation());

        // Assert
        exception.ParamName.ShouldBe("error");
    }

    [Fact]
    public void HasMetadata_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError? error = null;

        // Act
        var exception = Should.Throw<ArgumentNullException>(() => error!.HasMetadata("key"));

        // Assert
        exception.ParamName.ShouldBe("error");
    }

    [Fact]
    public void HasMetadata_WhenKeyIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError error = new CriticalTestError();

        // Act
        var exception = Should.Throw<ArgumentNullException>(() => error.HasMetadata(null!));

        // Assert
        exception.ParamName.ShouldBe("key");
    }

    [Fact]
    public void TryGetMetadata_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError? error = null;

        // Act
        var exception = Should.Throw<ArgumentNullException>(() => error!.TryGetMetadata<int>("key"));

        // Assert
        exception.ParamName.ShouldBe("error");
    }

    [Fact]
    public void TryGetMetadata_WhenKeyIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError error = new CriticalTestError();

        // Act
        var exception = Should.Throw<ArgumentNullException>(() => error.TryGetMetadata<int>(null!));

        // Assert
        exception.ParamName.ShouldBe("key");
    }

    [Fact]
    public void ToException_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError? error = null;

        // Act
        var exception = Should.Throw<ArgumentNullException>(() => error!.ToException());

        // Assert
        exception.ParamName.ShouldBe("error");
    }

    private sealed record SeverityError(ErrorSeverity Level)
        : Error(ErrorCode.From("TEST.SEVERITY"), "Severity error")
    {
        public override ErrorSeverity Severity => Level;
    }

    private sealed record MetadataError(IReadOnlyDictionary<string, object?> Values)
        : Error(ErrorCode.From("TEST.METADATA"), "Metadata error")
    {
        public override IReadOnlyDictionary<string, object?> Metadata => Values;
    }
}
