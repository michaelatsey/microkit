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
        Converters = { new ResultJsonConverterFactory(includeMetadata: true) },
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
    public void Deserialize_WithoutOptions_ThrowsNotSupportedExceptionNamingTheFactory()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":42}""";

        // Act
        var exception = Should.Throw<NotSupportedException>(() => JsonSerializer.Deserialize<Result<int>>(json));

        // Assert: the serializer may wrap the converter's exception; its message, or an inner one, names the factory
        exception.ToString().ShouldContain(nameof(ResultJsonConverterFactory));
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
    public void Serialize_WhenCollectionHoldsASystemFaultWithoutOptions_RedactsEveryFaultMessageAndKeepsTheOthers()
    {
        // Arrange
        var errors = ErrorCollection.From(
            new FaultError(ErrorCategory.Technical, "Host db-7 refused the login of sa"),
            new NotFoundTestError(7));
        var result = Result.Failure<int>(errors);

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert: the collection is Technical, so its joined message is redacted too
        json.ShouldNotContain("db-7");
        using var document = JsonDocument.Parse(json);
        var error = document.RootElement.GetProperty("error");
        error.GetProperty("message").GetString().ShouldBe("The server could not complete the request.");
        var inner = error.GetProperty("errors");
        inner[0].GetProperty("message").GetString().ShouldBe("The server could not complete the request.");
        inner[1].GetProperty("message").GetString().ShouldBe("Item 7 not found");
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
        // Arrange: includeMetadata is for in-process storage, never a response: it writes everything
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
        thrown.ShouldNotBeNull();
        (thrown is JsonException or NotSupportedException).ShouldBeTrue(thrown.ToString());
    }

    private sealed record FaultError(ErrorCategory Kind, string Text) : Error(ErrorCode.From("TEST.FAULT"), Text)
    {
        public override ErrorCategory Category => Kind;
    }

    private sealed record Envelope(Result<string> Outcome);
}
