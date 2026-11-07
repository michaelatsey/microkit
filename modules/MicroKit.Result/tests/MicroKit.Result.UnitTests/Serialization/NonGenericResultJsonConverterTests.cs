using System.Text.Json;
using MicroKit.Result.Serialization;
using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Serialization;

// #184 Phase 5: the non-generic Result has its own reflection-free converter, carried by its [JsonConverter]
// attribute, so it serializes with or without a registered factory, never with metadata (ADR-RESULT-001 D12).
// #195: without a registered converter, a fault of the system is written with the generic message.
public sealed class NonGenericResultJsonConverterTests
{
    private static readonly JsonSerializerOptions FactoryOptions = new()
    {
        Converters = { new ResultJsonConverterFactory() },
    };

    private static readonly JsonSerializerOptions DefaultOptions = new();

    private static readonly JsonSerializerOptions MetadataFactoryOptions = new()
    {
        Converters = { new ResultJsonConverterFactory(includeMetadata: true) },
    };

    private static readonly JsonSerializerOptions MetadataConverterOptions = new()
    {
        Converters = { new ResultJsonConverter(includeMetadata: true) },
    };

    // ── Write ─────────────────────────────────────────────────────────────

    [Fact]
    public void Write_WhenSuccess_WritesIsSuccessOnly()
    {
        // Arrange
        var result = Result.Success();

        // Act
        var json = JsonSerializer.Serialize(result, FactoryOptions);

        // Assert
        json.ShouldBe("""{"isSuccess":true}""");
    }

    [Fact]
    public void Write_WhenFailure_WritesCodeMessageCategoryAndSeverity()
    {
        // Arrange
        var result = Result.Failure(new NotFoundTestError(7));

        // Act
        var json = JsonSerializer.Serialize(result, FactoryOptions);

        // Assert
        json.ShouldBe("""{"isSuccess":false,"error":{"code":"TEST.NOT_FOUND","message":"Item 7 not found","category":"NotFound","severity":"Error"}}""");
    }

    [Fact]
    public void Write_WhenFailureIsValidationError_WritesPropertyName()
    {
        // Arrange
        var result = Result.Failure(ValidationError.Custom("Email", "Email is invalid"));

        // Act
        var json = JsonSerializer.Serialize(result, FactoryOptions);

        // Assert
        json.ShouldBe("""{"isSuccess":false,"error":{"code":"VALIDATION.EMAIL","message":"Email is invalid","category":"Validation","severity":"Warning","propertyName":"Email"}}""");
    }

    [Fact]
    public void Write_WhenFailureIsErrorCollection_WritesInnerErrors()
    {
        // Arrange
        var result = Result.Failure(ErrorCollection.From(new NotFoundTestError(1), new OtherError()));

        // Act
        var json = JsonSerializer.Serialize(result, FactoryOptions);

        // Assert
        json.ShouldBe(
            """{"isSuccess":false,"error":{"code":"MULTI.ERROR","message":"Item 1 not found; Other error","category":"NotFound","severity":"Error","errors":[{"code":"TEST.NOT_FOUND","message":"Item 1 not found","category":"NotFound","severity":"Error"},{"code":"TEST.OTHER","message":"Other error","category":"Conflict","severity":"Error"}]}}""");
    }

    [Fact]
    public void Write_WhenFailureHasMetadata_DoesNotWriteMetadata()
    {
        // Arrange
        var result = Result.Failure(new CriticalTestError());

        // Act
        var json = JsonSerializer.Serialize(result, FactoryOptions);

        // Assert: #195, a default converter writes a fault of the system with the generic message
        json.ShouldBe("""{"isSuccess":false,"error":{"code":"TEST.CRITICAL","message":"The server could not complete the request.","category":"Unavailable","severity":"Critical"}}""");
    }

    [Fact]
    public void Write_WhenDefault_ThrowsJsonException()
    {
        // Arrange
        Result result = default;

        // Act + Assert
        var thrown = Should.Throw<JsonException>(() => JsonSerializer.Serialize(result, FactoryOptions));
        thrown.Message.ShouldContain("uninitialized (default) Result");
    }

    [Fact]
    public void Write_WhenWriterIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var converter = new ResultJsonConverter();

        // Act + Assert
        Should.Throw<ArgumentNullException>(() => converter.Write(null!, Result.Success(), DefaultOptions));
    }

    // ── Read ──────────────────────────────────────────────────────────────

    [Fact]
    public void Read_WhenSuccess_ReturnsSuccess()
    {
        // Arrange
        const string json = """{"isSuccess":true}""";

        // Act
        var result = JsonSerializer.Deserialize<Result>(json, FactoryOptions);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
    }

    [Fact]
    public void Read_WhenFailure_ReturnsDeserializedError()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"TEST.OTHER","message":"Other error","category":"Conflict","severity":"Critical"}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result>(json, FactoryOptions);

        // Assert
        result.IsFailure.ShouldBeTrue();
        var error = result.Error.ShouldBeOfType<DeserializedError>();
        error.Code.Value.ShouldBe("TEST.OTHER");
        error.Message.ShouldBe("Other error");
        error.Category.ShouldBe(ErrorCategory.Conflict);
        error.Severity.ShouldBe(ErrorSeverity.Critical);
        error.Metadata.ShouldBeEmpty();
    }

    [Fact]
    public void Read_WhenErrorBeforeIsSuccess_ReturnsFailure()
    {
        // Arrange
        const string json = """{"error":{"code":"TEST.OTHER","message":"Other error"},"isSuccess":false}""";

        // Act
        var result = JsonSerializer.Deserialize<Result>(json, FactoryOptions);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.Value.ShouldBe("TEST.OTHER");
    }

    [Fact]
    public void Read_WhenUnknownPropertiesPresent_SkipsThem()
    {
        // Arrange
        const string json = """{"extra":{"nested":[1,{"deep":true}]},"isSuccess":true,"trailing":"ignored"}""";

        // Act
        var result = JsonSerializer.Deserialize<Result>(json, FactoryOptions);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"isSuccess":true,"value":1}""")]
    [InlineData("""{"isSuccess":true,"value":null}""")]
    [InlineData("""{"value":{},"isSuccess":false,"error":{"code":"A.B","message":"m"}}""")]
    [InlineData("""{"isSuccess":true,"isSuccess":true}""")]
    [InlineData("""{"isSuccess":false,"isSuccess":true}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"A.B","message":"m"},"error":{"code":"C.D","message":"n"}}""")]
    [InlineData("""{"isSuccess":true,"error":{"code":"A.B","message":"m"}}""")]
    [InlineData("""{"isSuccess":false}""")]
    [InlineData("""{"error":{"code":"A.B","message":"m"}}""")]
    [InlineData("""{}""")]
    [InlineData("""{"isSuccess":"true"}""")]
    [InlineData("""{"isSuccess":false,"error":"boom"}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"","message":"m"}}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"A.B","message":"m","errors":[]}}""")]
    public void Read_WhenPayloadIsNotAValidResult_ThrowsJsonException(string json)
    {
        // Arrange + Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result>(json, FactoryOptions));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("true")]
    public void Read_WhenTokenIsNotObject_ThrowsJsonException(string json)
    {
        // Arrange + Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result>(json, FactoryOptions));
    }

    [Fact]
    public void Read_WhenValuePresent_ThrowsJsonExceptionNamingValue()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":1}""";

        // Act + Assert
        var thrown = Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result>(json, FactoryOptions));
        thrown.Message.ShouldContain("'value'");
    }

    // ── Round trip ────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_WhenSuccess_ReturnsEqualSuccess()
    {
        // Arrange
        var original = Result.Success();
        var json = JsonSerializer.Serialize(original, FactoryOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result>(json, FactoryOptions);

        // Assert
        read.IsSuccess.ShouldBeTrue();
        read.ShouldBe(original);
    }

    [Fact]
    public void RoundTrip_WhenFailure_KeepsCodeCategoryAndSeverity()
    {
        // Arrange
        var original = Result.Failure(new CriticalTestError());
        var json = JsonSerializer.Serialize(original, FactoryOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result>(json, FactoryOptions);

        // Assert: #195, a default converter writes a fault of the system (Unavailable) with the generic message
        read.IsFailure.ShouldBeTrue();
        read.Error.Code.Value.ShouldBe("TEST.CRITICAL");
        read.Error.Message.ShouldBe("The server could not complete the request.");
        read.Error.Category.ShouldBe(ErrorCategory.Unavailable);
        read.Error.Severity.ShouldBe(ErrorSeverity.Critical);
        read.Error.Metadata.ShouldBeEmpty();
    }

    [Fact]
    public void RoundTrip_WhenFailureIsValidationError_ReturnsDeserializedValidationError()
    {
        // Arrange
        var original = Result.Failure(ValidationError.Required("Name"));
        var json = JsonSerializer.Serialize(original, FactoryOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result>(json, FactoryOptions);

        // Assert
        read.IsFailure.ShouldBeTrue();
        var error = read.Error.ShouldBeOfType<DeserializedValidationError>();
        error.PropertyName.ShouldBe("Name");
        error.Code.Value.ShouldBe("VALIDATION.NAME");
        error.Category.ShouldBe(ErrorCategory.Validation);
        error.Severity.ShouldBe(ErrorSeverity.Warning);
    }

    // ── [JsonConverter] attribute, no converter registered ────────────────

    [Fact]
    public void Serialize_WhenNoConverterRegisteredAndSuccess_WritesIsSuccessWithoutThrowing()
    {
        // Arrange
        var result = Result.Success();

        // Act
        var json = JsonSerializer.Serialize(result, DefaultOptions);

        // Assert
        json.ShouldBe("""{"isSuccess":true}""");
    }

    [Fact]
    public void Serialize_WhenNoConverterRegisteredAndFailureHasMetadata_WritesErrorWithoutMetadata()
    {
        // Arrange
        var result = Result.Failure(new CriticalTestError());

        // Act
        var json = JsonSerializer.Serialize(result, DefaultOptions);

        // Assert
        // #195: with no converter registered, a fault of the system (Unavailable) is written with the generic message
        json.ShouldBe("""{"isSuccess":false,"error":{"code":"TEST.CRITICAL","message":"The server could not complete the request.","category":"Unavailable","severity":"Critical"}}""");
    }

    [Fact]
    public void Serialize_WhenNoConverterRegisteredAndDefault_ThrowsJsonException()
    {
        // Arrange
        Result result = default;

        // Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Serialize(result, DefaultOptions));
    }

    [Fact]
    public void Deserialize_WhenNoConverterRegistered_ReadsFailure()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"TEST.OTHER","message":"Other error","category":"Conflict"}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result>(json, DefaultOptions);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<DeserializedError>();
        result.Error.Category.ShouldBe(ErrorCategory.Conflict);
    }

    [Fact]
    public void Deserialize_WhenNoConverterRegisteredAndPayloadInvalid_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":true,"error":{"code":"A.B","message":"m"}}""";

        // Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result>(json, DefaultOptions));
    }

    [Fact]
    public void Serialize_WhenResultIsPropertyOfAType_UsesAttributeConverterWithoutMetadata()
    {
        // Arrange
        var envelope = new Envelope("save", Result.Failure(new CriticalTestError()));

        // Act
        var json = JsonSerializer.Serialize(envelope, DefaultOptions);

        // Assert
        // #195: with no converter registered, a fault of the system (Unavailable) is written with the generic message
        json.ShouldBe("""{"Name":"save","Outcome":{"isSuccess":false,"error":{"code":"TEST.CRITICAL","message":"The server could not complete the request.","category":"Unavailable","severity":"Critical"}}}""");
    }

    [Fact]
    public void Serialize_WhenSuccessIsPropertyOfAType_WritesIsSuccessOnly()
    {
        // Arrange
        var envelope = new Envelope("save", Result.Success());

        // Act
        var json = JsonSerializer.Serialize(envelope, DefaultOptions);

        // Assert
        json.ShouldBe("""{"Name":"save","Outcome":{"isSuccess":true}}""");
    }

    [Fact]
    public void RoundTrip_WhenResultIsPropertyOfAType_KeepsResult()
    {
        // Arrange
        var original = new Envelope("save", Result.Failure(new OtherError()));
        var json = JsonSerializer.Serialize(original, DefaultOptions);

        // Act
        var read = JsonSerializer.Deserialize<Envelope>(json, DefaultOptions);

        // Assert
        read.ShouldNotBeNull();
        read.Name.ShouldBe("save");
        read.Outcome.IsFailure.ShouldBeTrue();
        read.Outcome.Error.Code.Value.ShouldBe("TEST.OTHER");
        read.Outcome.Error.Category.ShouldBe(ErrorCategory.Conflict);
    }

    // ── Options converters take precedence over the type attribute ────────

    [Fact]
    public void Serialize_WhenFactoryWithMetadataRegistered_TakesPrecedenceAndWritesMetadata()
    {
        // Arrange
        var result = Result.Failure(new CriticalTestError());

        // Act
        var json = JsonSerializer.Serialize(result, MetadataFactoryOptions);

        // Assert
        json.ShouldBe("""{"isSuccess":false,"error":{"code":"TEST.CRITICAL","message":"Critical error","category":"Unavailable","severity":"Critical","metadata":{"retryAfter":30}}}""");
    }

    [Fact]
    public void Serialize_WhenConverterWithMetadataRegistered_TakesPrecedenceAndWritesMetadata()
    {
        // Arrange
        var result = Result.Failure(new CriticalTestError());

        // Act
        var json = JsonSerializer.Serialize(result, MetadataConverterOptions);

        // Assert
        json.ShouldBe("""{"isSuccess":false,"error":{"code":"TEST.CRITICAL","message":"Critical error","category":"Unavailable","severity":"Critical","metadata":{"retryAfter":30}}}""");
    }

    [Fact]
    public void Serialize_WhenFactoryWithMetadataRegisteredAndResultIsProperty_WritesMetadata()
    {
        // Arrange
        var envelope = new Envelope("save", Result.Failure(new CriticalTestError()));

        // Act
        var json = JsonSerializer.Serialize(envelope, MetadataFactoryOptions);

        // Assert
        json.ShouldBe("""{"Name":"save","Outcome":{"isSuccess":false,"error":{"code":"TEST.CRITICAL","message":"Critical error","category":"Unavailable","severity":"Critical","metadata":{"retryAfter":30}}}}""");
    }

    [Fact]
    public void RoundTrip_WhenFactoryWithMetadataRegistered_KeepsMetadata()
    {
        // Arrange
        var json = JsonSerializer.Serialize(Result.Failure(new CriticalTestError()), MetadataFactoryOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result>(json, MetadataFactoryOptions);

        // Assert
        read.IsFailure.ShouldBeTrue();
        read.Error.Metadata.ShouldBe(new Dictionary<string, object?> { ["retryAfter"] = 30L });
    }

    // ── Factory ───────────────────────────────────────────────────────────

    [Fact]
    public void CreateConverter_WhenResult_ReturnsNonGenericConverter()
    {
        // Arrange
        var factory = new ResultJsonConverterFactory();

        // Act
        var converter = factory.CreateConverter(typeof(Result), FactoryOptions);

        // Assert
        converter.ShouldBeOfType<ResultJsonConverter>();
    }

    // ── #195: the converter Result carries reaches clients by default ─────

    [Theory]
    [InlineData(ErrorCategory.Technical)]
    [InlineData(ErrorCategory.External)]
    [InlineData(ErrorCategory.Unavailable)]
    [InlineData(ErrorCategory.Timeout)]
    [InlineData(ErrorCategory.NotSupported)]
    [InlineData((ErrorCategory)999)]
    public void Write_WithoutConverterRegistered_WhenSystemFault_WritesAGenericMessage(ErrorCategory category)
    {
        // Arrange
        var result = Result.Failure(new FaultError(category, "Host db-7 refused the login of sa"));

        // Act
        var json = JsonSerializer.Serialize(result, DefaultOptions);

        // Assert
        json.ShouldNotContain("db-7");
        using var document = JsonDocument.Parse(json);
        var error = document.RootElement.GetProperty("error");
        error.GetProperty("message").GetString().ShouldBe("The server could not complete the request.");
        error.GetProperty("code").GetString().ShouldBe("TEST.FAULT");
    }

    [Fact]
    public void Write_WithoutConverterRegistered_WhenNotFound_KeepsItsMessage()
    {
        // Arrange
        var result = Result.Failure(new NotFoundTestError(7));

        // Act
        var json = JsonSerializer.Serialize(result, DefaultOptions);

        // Assert
        json.ShouldBe("""{"isSuccess":false,"error":{"code":"TEST.NOT_FOUND","message":"Item 7 not found","category":"NotFound","severity":"Error"}}""");
    }

    [Fact]
    public void Write_WithDefaultFactoryRegistered_WhenSystemFault_WritesAGenericMessage()
    {
        // Arrange: the factory a host registers for its endpoints writes what a client may read
        var result = Result.Failure(new FaultError(ErrorCategory.Technical, "Host db-7 refused the login of sa"));

        // Act
        var json = JsonSerializer.Serialize(result, FactoryOptions);

        // Assert
        json.ShouldNotContain("db-7");
    }

    [Theory]
    [MemberData(nameof(MetadataOptions))]
    public void Write_WithMetadataConverterRegistered_WhenSystemFault_KeepsItsMessage(JsonSerializerOptions options)
    {
        // Arrange: includeMetadata is for in-process storage, never a response: it writes everything
        var result = Result.Failure(new FaultError(ErrorCategory.Technical, "Host db-7 refused the login of sa"));

        // Act
        var json = JsonSerializer.Serialize(result, options);

        // Assert
        json.ShouldContain("Host db-7 refused the login of sa");
    }

    public static TheoryData<JsonSerializerOptions> MetadataOptions() => new() { MetadataFactoryOptions, MetadataConverterOptions };

    [Fact]
    public void RoundTrip_WithoutConverterRegistered_WhenSystemFault_ReadsTheGenericMessage()
    {
        // Arrange
        var json = JsonSerializer.Serialize(Result.Failure(new FaultError(ErrorCategory.Technical, "secret")), DefaultOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result>(json, DefaultOptions);

        // Assert
        read.IsFailure.ShouldBeTrue();
        read.Error.Code.ShouldBe(ErrorCode.From("TEST.FAULT"));
        read.Error.Category.ShouldBe(ErrorCategory.Technical);
        read.Error.Message.ShouldBe("The server could not complete the request.");
    }

    private sealed record FaultError(ErrorCategory Kind, string Text) : Error(ErrorCode.From("TEST.FAULT"), Text)
    {
        public override ErrorCategory Category => Kind;
    }

    private sealed record Envelope(string Name, Result Outcome);
}
