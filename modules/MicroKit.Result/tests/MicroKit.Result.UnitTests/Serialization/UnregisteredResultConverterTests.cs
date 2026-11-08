using System.Text.Json;
using MicroKit.Result.Serialization;
using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Serialization;

// #195 item 6: with no converter registered, Result<T> still writes the documented envelope, never metadata, and
// reading it names the factory to register. A converter registered in the options takes precedence.
public sealed class UnregisteredResultConverterTests
{
    private static readonly JsonSerializerOptions MetadataFactoryOptions = new()
    {
        Converters = { new ResultJsonConverterFactory(ResultJsonView.Full) },
    };

    private static readonly JsonSerializerOptions ClosedConverterOptions = new()
    {
        Converters = { new ResultJsonConverter<int>() },
    };

    private static readonly JsonSerializerOptions FactoryOptions = new()
    {
        Converters = { new ResultJsonConverterFactory() },
    };

    [Fact]
    public void Serialize_WhenSuccessWithoutOptions_WritesTheEnvelope()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        json.ShouldBe("""{"isSuccess":true,"value":42}""");
    }

    [Fact]
    public void Serialize_WhenFailureWithoutOptions_WritesTheErrorEnvelope()
    {
        // Arrange
        var result = Result.Failure<int>(new NotFoundTestError(7));

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        json.ShouldBe("""{"isSuccess":false,"error":{"code":"TEST.NOT_FOUND","message":"Item 7 not found","category":"NotFound","severity":"Error"}}""");
    }

    [Fact]
    public void Serialize_WhenFailureWithMetadataWithoutOptions_OmitsMetadata()
    {
        // Arrange
        var result = Result.Failure<int>(new CriticalTestError());

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        json.ShouldNotContain("metadata", Case.Insensitive);
        json.ShouldNotContain("retryAfter", Case.Insensitive);
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("isSuccess").GetBoolean().ShouldBeFalse();
        document.RootElement.GetProperty("error").GetProperty("code").GetString().ShouldBe("TEST.CRITICAL");
    }

    [Fact]
    public void Serialize_WhenResultIsAPropertyWithoutOptions_WritesTheEnvelope()
    {
        // Arrange
        var envelope = new Envelope(Result.Success("Pen"));

        // Act
        var json = JsonSerializer.Serialize(envelope);

        // Assert
        json.ShouldBe("""{"Outcome":{"isSuccess":true,"value":"Pen"}}""");
    }

    [Fact]
    public void Serialize_WhenDefaultWithoutOptions_ThrowsJsonException()
    {
        // Arrange
        Result<int> result = default;

        // Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Serialize(result));
    }

    [Fact]
    public void Deserialize_WithoutOptions_ThrowsJsonExceptionNamingTheFactory()
    {
        // Arrange: #198 review, what it wrote must fail on read as an unreadable payload does, a JsonException,
        // which a reader that catches JsonException (a cache, a request binder) handles as an unreadable entry
        const string json = """{"isSuccess":true,"value":42}""";

        // Act
        var exception = Record.Exception(() => JsonSerializer.Deserialize<Result<int>>(json));

        // Assert
        exception.ShouldBeOfType<JsonException>();
        exception.Message.ShouldContain(nameof(ResultJsonConverterFactory));
    }

    [Fact]
    public void RoundTrip_WithoutOptions_WhenWrittenThenRead_FailsWithJsonException()
    {
        // Arrange
        var json = JsonSerializer.Serialize(Result.Success(42));

        // Act
        var exception = Record.Exception(() => JsonSerializer.Deserialize<Result<int>>(json));

        // Assert: never a NotSupportedException
        exception.ShouldBeOfType<JsonException>();
    }

    [Fact]
    public void Serialize_WhenFactoryWithMetadataIsRegistered_TakesPrecedenceAndWritesMetadata()
    {
        // Arrange
        var result = Result.Failure<int>(new CriticalTestError());

        // Act
        var json = JsonSerializer.Serialize(result, MetadataFactoryOptions);

        // Assert
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("error").GetProperty("metadata").GetProperty("retryAfter").GetInt32().ShouldBe(30);
    }

    [Fact]
    public void Deserialize_WhenFactoryIsRegistered_ReadsTheEnvelope()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":42}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, FactoryOptions);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Theory]
    [InlineData(ErrorCategory.Technical)]
    [InlineData(ErrorCategory.External)]
    [InlineData(ErrorCategory.Unavailable)]
    [InlineData(ErrorCategory.Timeout)]
    [InlineData(ErrorCategory.NotSupported)]
    [InlineData((ErrorCategory)999)]
    public void Serialize_WhenSystemFaultWithoutOptions_WritesAGenericMessage(ErrorCategory category)
    {
        // Arrange: a fault of the system may describe it; the envelope reaches clients by default
        var result = Result.Failure<int>(new FaultError(category, "Host db-7 refused the login of sa"));

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        json.ShouldNotContain("db-7");
        using var document = JsonDocument.Parse(json);
        var error = document.RootElement.GetProperty("error");
        error.GetProperty("message").GetString().ShouldBe("The server could not complete the request.");
        error.GetProperty("code").GetString().ShouldBe("TEST.FAULT");
    }

    [Fact]
    public void Serialize_WhenCollectionHoldsASystemFaultWithoutOptions_RedactsItAndNamesOnlyTheFaults()
    {
        // Arrange
        var errors = ErrorCollection.From(
            new FaultError(ErrorCategory.Technical, "Host db-7 refused the login of sa"),
            new NotFoundTestError(7));
        var result = Result.Failure<int>(errors);

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert: the collection is Technical, so its message is redacted, and it names only its faults
        json.ShouldNotContain("db-7");
        json.ShouldNotContain("Item 7 not found");
        using var document = JsonDocument.Parse(json);
        var error = document.RootElement.GetProperty("error");
        error.GetProperty("message").GetString().ShouldBe("The server could not complete the request.");
        var inner = error.GetProperty("errors");
        inner.GetArrayLength().ShouldBe(1);
        inner[0].GetProperty("message").GetString().ShouldBe("The server could not complete the request.");
    }

    [Fact]
    public void Serialize_WhenSystemFaultWithDefaultFactoryRegistered_WritesAGenericMessage()
    {
        // Arrange: the factory a host registers for its endpoints writes what a client may read
        var result = Result.Failure<int>(new FaultError(ErrorCategory.Technical, "Host db-7 refused the login of sa"));

        // Act
        var json = JsonSerializer.Serialize(result, FactoryOptions);

        // Assert
        json.ShouldNotContain("db-7");
        json.ShouldContain("The server could not complete the request.");
    }

    [Fact]
    public void Serialize_WhenSystemFaultWithMetadataFactoryRegistered_KeepsItsMessage()
    {
        // Arrange: ResultJsonView.Full is for in-process storage, never a response: it writes everything
        var result = Result.Failure<int>(new FaultError(ErrorCategory.Technical, "Host db-7 refused the login of sa"));

        // Act
        var json = JsonSerializer.Serialize(result, MetadataFactoryOptions);

        // Assert
        json.ShouldContain("Host db-7 refused the login of sa");
    }

    [Fact]
    public void Serialize_WhenSystemFaultWithDefaultClosedConverterRegistered_WritesAGenericMessage()
    {
        // Arrange
        var result = Result.Failure<int>(new FaultError(ErrorCategory.Technical, "Host db-7 refused the login of sa"));

        // Act
        var json = JsonSerializer.Serialize(result, ClosedConverterOptions);

        // Assert
        json.ShouldNotContain("db-7");
    }

    [Fact]
    public void Deserialize_WhenJsonIsNullWithoutOptions_ThrowsAndNeverReturnsDefault()
    {
        // Arrange
        const string json = "null";

        // Act
        var thrown = Record.Exception(() => JsonSerializer.Deserialize<Result<int>>(json));

        // Assert: never an uninitialized result read as a value
        thrown.ShouldBeOfType<JsonException>();
    }

    [Fact]
    public void Serialize_WhenUnauthorizedCollectionHoldsAValidationErrorWithoutOptions_WritesNoValidationDetail()
    {
        // Arrange: ToHttpResult answers 401 and names neither the field nor its message
        var errors = ErrorCollection.From(new AccessError(ErrorCategory.Unauthorized), ValidationError.Required("Email"));
        var result = Result.Failure<int>(errors);

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        json.ShouldNotContain("Email");
        json.ShouldNotContain("propertyName");
        using var document = JsonDocument.Parse(json);
        var error = document.RootElement.GetProperty("error");
        error.GetProperty("category").GetString().ShouldBe("Unauthorized");
        error.GetProperty("message").GetString().ShouldBe("Sign in first.");
        var inner = error.GetProperty("errors");
        inner.GetArrayLength().ShouldBe(1);
        inner[0].GetProperty("code").GetString().ShouldBe("TEST.ACCESS");
    }

    [Fact]
    public void Serialize_WhenTechnicalCollectionHoldsOtherErrorsWithoutOptions_WritesOnlyItsFaults()
    {
        // Arrange
        var errors = ErrorCollection.From(
            ValidationError.Required("Email"),
            new NotFoundTestError(7),
            new FaultError(ErrorCategory.Technical, "Host db-7 refused the login of sa"));
        var result = Result.Failure<int>(errors);

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        json.ShouldNotContain("Email");
        json.ShouldNotContain("Item 7 not found");
        json.ShouldNotContain("db-7");
        using var document = JsonDocument.Parse(json);
        var inner = document.RootElement.GetProperty("error").GetProperty("errors");
        inner.GetArrayLength().ShouldBe(1);
        inner[0].GetProperty("code").GetString().ShouldBe("TEST.FAULT");
    }

    [Fact]
    public void Serialize_WhenNestedCollectionsWithoutOptions_FlattensTheErrorsOfTheCategory()
    {
        // Arrange
        var errors = ErrorCollection.From(
            ErrorCollection.From(new AccessError(ErrorCategory.Unauthorized), ValidationError.Required("Email")),
            new AccessError(ErrorCategory.Unauthorized));
        var result = Result.Failure<int>(errors);

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        using var document = JsonDocument.Parse(json);
        var inner = document.RootElement.GetProperty("error").GetProperty("errors");
        inner.EnumerateArray().Select(e => e.GetProperty("code").GetString()).ShouldBe(["TEST.ACCESS", "TEST.ACCESS"]);
        inner.EnumerateArray().Any(e => e.TryGetProperty("errors", out _)).ShouldBeFalse();
    }

    [Fact]
    public void Serialize_WhenValidationCollectionWithoutOptions_KeepsEveryFieldError()
    {
        // Arrange
        var errors = ErrorCollection.From(ValidationError.Required("Email"), ValidationError.Required("Name"));
        var result = Result.Failure<int>(errors);

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        json.ShouldContain("\"propertyName\":\"Email\"");
        json.ShouldContain("\"propertyName\":\"Name\"");
    }

    [Fact]
    public void Serialize_WhenValidationErrorHasAnotherCategoryWithoutOptions_WritesNoPropertyName()
    {
        // Arrange: a third-party IValidationError whose category is not an input category
        var result = Result.Failure<int>(new ForeignValidationError());

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        json.ShouldNotContain("propertyName");
        json.ShouldNotContain("Secret");
    }

    [Fact]
    public void Serialize_WhenUnauthorizedCollectionWithMetadataFactory_KeepsEveryError()
    {
        // Arrange: ResultJsonView.Full is for in-process storage, which keeps the whole collection
        var errors = ErrorCollection.From(new AccessError(ErrorCategory.Unauthorized), ValidationError.Required("Email"));

        // Act
        var json = JsonSerializer.Serialize(Result.Failure<int>(errors), MetadataFactoryOptions);

        // Assert
        json.ShouldContain("\"propertyName\":\"Email\"");
    }

    private sealed record AccessError(ErrorCategory Kind) : Error(ErrorCode.From("TEST.ACCESS"), "Sign in first.")
    {
        public override ErrorCategory Category => Kind;
    }

    private sealed record ForeignValidationError() : Error(ErrorCode.From("TEST.FOREIGN"), "Not found."), IValidationError
    {
        public override ErrorCategory Category => ErrorCategory.NotFound;

        public string PropertyName => "Secret";
    }

    private sealed record FaultError(ErrorCategory Kind, string Text) : Error(ErrorCode.From("TEST.FAULT"), Text)
    {
        public override ErrorCategory Category => Kind;
    }

    private sealed record Envelope(Result<string> Outcome);
}
