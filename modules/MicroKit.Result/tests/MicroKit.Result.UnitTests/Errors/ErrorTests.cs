using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Errors;

public sealed class ErrorTests
{
    [Fact]
    public void Category_WhenNotOverridden_IsTechnical()
    {
        // Arrange
        var error = new TestError();

        // Act
        var category = error.Category;

        // Assert
        category.ShouldBe(ErrorCategory.Technical);
    }

    [Fact]
    public void Severity_WhenNotOverridden_IsError()
    {
        // Arrange
        var error = new TestError();

        // Act
        var severity = error.Severity;

        // Assert
        severity.ShouldBe(ErrorSeverity.Error);
    }

    [Fact]
    public void Metadata_WhenNotOverridden_IsEmpty()
    {
        // Arrange
        var error = new TestError();

        // Act
        var metadata = error.Metadata;

        // Assert
        metadata.ShouldBeEmpty();
        metadata.ShouldBeSameAs(ErrorMetadata.Empty);
    }

    [Fact]
    public void Constructor_WhenCalled_ExposesCodeAndMessage()
    {
        // Arrange
        var code = ErrorCode.From("ORDER.PAYMENT.DECLINED");

        // Act
        var error = new ConfigurableError(code, "Payment declined");

        // Assert
        error.Code.ShouldBe(code);
        error.Message.ShouldBe("Payment declined");
    }

    [Fact]
    public void Category_WhenOverridden_ReturnsOverride()
    {
        // Arrange
        var error = new NotFoundTestError(7);

        // Act
        var category = error.Category;

        // Assert
        category.ShouldBe(ErrorCategory.NotFound);
    }

    [Fact]
    public void Overrides_WhenCategorySeverityAndMetadataOverridden_ReturnsOverrides()
    {
        // Arrange
        var error = new CriticalTestError();

        // Act
        var category = error.Category;
        var severity = error.Severity;
        var metadata = error.Metadata;

        // Assert
        category.ShouldBe(ErrorCategory.Unavailable);
        severity.ShouldBe(ErrorSeverity.Critical);
        metadata.Count.ShouldBe(1);
        metadata["retryAfter"].ShouldBe(30);
    }

    [Fact]
    public void Equals_WhenSameTypeAndValues_ReturnsTrue()
    {
        // Arrange
        var first = new TestError("same");
        var second = new TestError("same");

        // Act
        var equal = first.Equals(second);

        // Assert
        equal.ShouldBeTrue();
        (first == second).ShouldBeTrue();
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Equals_WhenDifferentCode_ReturnsFalse()
    {
        // Arrange
        var first = new ConfigurableError(ErrorCode.From("A.CODE"), "message");
        var second = new ConfigurableError(ErrorCode.From("B.CODE"), "message");

        // Act
        var equal = first.Equals(second);

        // Assert
        equal.ShouldBeFalse();
        (first != second).ShouldBeTrue();
    }

    [Fact]
    public void Equals_WhenDifferentMessage_ReturnsFalse()
    {
        // Arrange
        var first = new ConfigurableError(ErrorCode.From("A.CODE"), "first");
        var second = new ConfigurableError(ErrorCode.From("A.CODE"), "second");

        // Act
        var equal = first.Equals(second);

        // Assert
        equal.ShouldBeFalse();
    }

    [Fact]
    public void Equals_WhenDifferentTypeWithSameCodeAndMessage_ReturnsFalse()
    {
        // Arrange
        var code = ErrorCode.From("A.CODE");
        Error first = new ConfigurableError(code, "message");
        Error second = new OtherConfigurableError(code, "message");

        // Act
        var equal = first.Equals(second);

        // Assert
        equal.ShouldBeFalse();
    }

    [Fact]
    public void With_WhenMessageChanged_KeepsCodeAndChangesMessage()
    {
        // Arrange
        var original = new ConfigurableError(ErrorCode.From("A.CODE"), "before");

        // Act
        var changed = original with { Message = "after" };

        // Assert
        changed.Code.ShouldBe(original.Code);
        changed.Message.ShouldBe("after");
        original.Message.ShouldBe("before");
    }

    private sealed record ConfigurableError(ErrorCode Code, string Message) : Error(Code, Message);

    private sealed record OtherConfigurableError(ErrorCode Code, string Message) : Error(Code, Message);
}
