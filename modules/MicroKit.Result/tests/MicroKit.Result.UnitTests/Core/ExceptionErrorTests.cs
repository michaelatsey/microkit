using System.Text.Json;
using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Core;

public sealed class ExceptionErrorTests
{
    private const string SecretMessage = "secret connection string";

    [Fact]
    public void Constructor_WhenExceptionIsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        Should.Throw<ArgumentNullException>(() => new ExceptionError(null!));
    }

    [Fact]
    public void Constructor_WhenCalled_KeepsTheExceptionInProcessBehindAGenericMessage()
    {
        // Arrange
        var exception = new InvalidOperationException(SecretMessage);

        // Act
        var error = new ExceptionError(exception);

        // Assert
        error.Exception.ShouldBeSameAs(exception);
        error.Code.ShouldBe(ErrorCode.Exception);
        error.Message.ShouldBe("An unexpected error occurred.");
        error.Category.ShouldBe(ErrorCategory.Technical);
        error.Severity.ShouldBe(ErrorSeverity.Error);
        error.Metadata.ShouldBeEmpty();
    }

    [Fact]
    public void Try_WhenFuncThrows_ErrorCarriesTheCaughtException()
    {
        // Arrange
        var exception = new InvalidOperationException(SecretMessage);

        // Act
        Result<int> result = Result<int>.Try(() => throw exception);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ExceptionError>().Exception.ShouldBeSameAs(exception);
    }

    [Fact]
    public void Serialize_WhenSerializedAsItsOwnType_DoesNotEmitTheException()
    {
        // Arrange
        var error = new ExceptionError(new InvalidOperationException(SecretMessage));

        // Act
        string json = JsonSerializer.Serialize(error);

        // Assert
        json.ShouldNotContain(SecretMessage);
        json.ShouldNotContain("\"Exception\":", Case.Sensitive);
    }

    [Fact]
    public void ToString_WhenCalled_DoesNotPrintTheException()
    {
        // Arrange
        var error = new ExceptionError(new InvalidOperationException(SecretMessage));

        // Act
        string text = error.ToString();

        // Assert
        text.ShouldNotContain(SecretMessage);
        text.ShouldNotContain(nameof(InvalidOperationException));
        text.ShouldContain("EXCEPTION");
    }

    [Fact]
    public void Try_WhenMapperReturnsNull_KeepsTheCaughtExceptionAsInner()
    {
        // Arrange
        var caught = new InvalidOperationException(SecretMessage);

        // Act
        var thrown = Should.Throw<ArgumentNullException>(() => Result<int>.Try(() => throw caught, _ => null!));

        // Assert
        thrown.InnerException.ShouldBeSameAs(caught);
    }
}
