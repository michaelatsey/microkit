using System.Globalization;
using System.Text.Json;
using MicroKit.Result.Serialization;
using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Serialization;

// #184 Phase 5: the error object keeps Severity, the inner errors of an ErrorCollection, PropertyName and, on
// opt-in (includeMetadata), Metadata across a round trip (ADR-RESULT-001 D1, D12).
public sealed class ErrorJsonRoundTripTests
{
    // The factory, as a consumer registers it.
    private static readonly JsonSerializerOptions FactoryMetadataOptions = new()
    {
        Converters = { new ResultJsonConverterFactory(includeMetadata: true) },
    };

    // The converter created directly: the encoding tests below do not depend on how the factory creates it.
    private static readonly JsonSerializerOptions MetadataOptions = new()
    {
        Converters = { new ResultJsonConverter<int>(includeMetadata: true) },
    };

    private static readonly JsonSerializerOptions DefaultFactoryOptions = new()
    {
        Converters = { new ResultJsonConverterFactory() },
    };

    private static readonly JsonSerializerOptions DefaultConverterOfTOptions = new()
    {
        Converters = { new ResultJsonConverter<int>() },
    };

    private static readonly Guid SampleId = new("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    // ── Severity and metadata ─────────────────────────────────────────────

    [Fact]
    public void RoundTrip_WhenFactoryIncludesMetadata_KeepsSeverityCategoryAndMetadata()
    {
        // Arrange
        var original = Result.Failure<int>(new CriticalTestError());
        var json = JsonSerializer.Serialize(original, FactoryMetadataOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result<int>>(json, FactoryMetadataOptions);

        // Assert
        json.ShouldBe("""{"isSuccess":false,"error":{"code":"TEST.CRITICAL","message":"Critical error","category":"Unavailable","severity":"Critical","metadata":{"retryAfter":30}}}""");
        read.IsFailure.ShouldBeTrue();
        var error = read.Error.ShouldBeOfType<DeserializedError>();
        error.Code.Value.ShouldBe("TEST.CRITICAL");
        error.Message.ShouldBe("Critical error");
        error.Category.ShouldBe(ErrorCategory.Unavailable);
        error.Severity.ShouldBe(ErrorSeverity.Critical);
        error.Metadata.ShouldBe(new Dictionary<string, object?> { ["retryAfter"] = 30L });
    }

    [Fact]
    public void Write_WhenIncludeMetadataAndMetadataEmpty_OmitsMetadata()
    {
        // Arrange
        var result = Result.Failure<int>(new OtherError());

        // Act
        var json = JsonSerializer.Serialize(result, MetadataOptions);

        // Assert
        json.ShouldBe("""{"isSuccess":false,"error":{"code":"TEST.OTHER","message":"Other error","category":"Conflict","severity":"Error"}}""");
    }

    [Fact]
    public void Write_WhenIncludeMetadata_WritesPrimitivesAsJsonValues()
    {
        // Arrange
        var metadata = ErrorMetadata.Create(
            new("text", "hello"),
            new("flag", true),
            new("off", false),
            new("int", 42),
            new("long", 9_000_000_000L),
            new("short", (short)-7),
            new("byte", (byte)255),
            new("uint", 4_000_000_000u),
            new("ulong", ulong.MaxValue),
            new("decimal", 12.50m),
            new("double", 1.25d),
            new("float", 0.5f),
            new("guid", SampleId),
            new("dateTime", new DateTime(2026, 11, 7, 10, 30, 0, DateTimeKind.Utc)),
            new("dateTimeOffset", new DateTimeOffset(2026, 11, 7, 10, 30, 0, TimeSpan.FromHours(1))),
            new("none", null));
        var result = Result.Failure<int>(new MetadataError(metadata));

        // Act
        var json = JsonSerializer.Serialize(result, MetadataOptions);

        // Assert: each value as its JSON primitive; metadata is an immutable dictionary, whose key order is not defined
        using var document = JsonDocument.Parse(json);
        using var expected = JsonDocument.Parse(
            """{"text":"hello","flag":true,"off":false,"int":42,"long":9000000000,"short":-7,"byte":255,"uint":4000000000,"ulong":18446744073709551615,"decimal":12.50,"double":1.25,"float":0.5,"guid":"3f2504e0-4f89-11d3-9a0c-0305e82c3301","dateTime":"2026-11-07T10:30:00Z","dateTimeOffset":"2026-11-07T10:30:00+01:00","none":null}""");
        var written = document.RootElement.GetProperty("error").GetProperty("metadata");
        JsonElement.DeepEquals(written, expected.RootElement).ShouldBeTrue(written.GetRawText());
        written.GetProperty("decimal").GetRawText().ShouldBe("12.50"); // DeepEquals compares numbers by value
    }

    [Fact]
    public void RoundTrip_WhenIncludeMetadata_ReadsPrimitivesAsStringLongDoubleBoolOrNull()
    {
        // Arrange
        var metadata = ErrorMetadata.Create(
            new("text", "hello"),
            new("flag", true),
            new("off", false),
            new("int", 42),
            new("long", 9_000_000_000L),
            new("ulong", ulong.MaxValue),
            new("decimal", 12.50m),
            new("double", 1.25d),
            new("guid", SampleId),
            new("dateTime", new DateTime(2026, 11, 7, 10, 30, 0, DateTimeKind.Utc)),
            new("dateTimeOffset", new DateTimeOffset(2026, 11, 7, 10, 30, 0, TimeSpan.FromHours(1))),
            new("none", null));
        var json = JsonSerializer.Serialize(Result.Failure<int>(new MetadataError(metadata)), MetadataOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result<int>>(json, MetadataOptions);

        // Assert
        read.IsFailure.ShouldBeTrue();
        var values = read.Error.Metadata;
        values.Count.ShouldBe(12);
        values["text"].ShouldBeOfType<string>().ShouldBe("hello");
        values["flag"].ShouldBeOfType<bool>().ShouldBeTrue();
        values["off"].ShouldBeOfType<bool>().ShouldBeFalse();
        values["int"].ShouldBeOfType<long>().ShouldBe(42L);
        values["long"].ShouldBeOfType<long>().ShouldBe(9_000_000_000L);
        values["ulong"].ShouldBeOfType<ulong>().ShouldBe(ulong.MaxValue);
        values["decimal"].ShouldBeOfType<double>().ShouldBe(12.5d);
        values["double"].ShouldBeOfType<double>().ShouldBe(1.25d);
        values["guid"].ShouldBeOfType<string>().ShouldBe("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
        values["dateTime"].ShouldBeOfType<string>().ShouldBe("2026-11-07T10:30:00Z");
        values["dateTimeOffset"].ShouldBeOfType<string>().ShouldBe("2026-11-07T10:30:00+01:00");
        values["none"].ShouldBeNull();
    }

    [Fact]
    public void RoundTrip_WhenMetadataValueIsNotPrimitive_KeepsItsString()
    {
        // Arrange
        var metadata = ErrorMetadata.Create("money", new Money(10, "EUR"));
        var json = JsonSerializer.Serialize(Result.Failure<int>(new MetadataError(metadata)), MetadataOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result<int>>(json, MetadataOptions);

        // Assert
        read.IsFailure.ShouldBeTrue();
        read.Error.Metadata["money"].ShouldBe("10 EUR");
    }

    [Theory]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(double.NegativeInfinity, "-Infinity")]
    public void Write_WhenMetadataDoubleIsNotFinite_WritesItsInvariantString(double value, string expected)
    {
        // Arrange
        var result = Result.Failure<int>(new MetadataError(ErrorMetadata.Create("value", value)));

        // Act
        var json = JsonSerializer.Serialize(result, MetadataOptions);

        // Assert
        using var document = JsonDocument.Parse(json);
        var written = document.RootElement.GetProperty("error").GetProperty("metadata").GetProperty("value");
        written.ValueKind.ShouldBe(JsonValueKind.String);
        written.GetString().ShouldBe(expected);
    }

    [Fact]
    public void Write_WhenMetadataFloatIsNotFinite_WritesItsInvariantString()
    {
        // Arrange
        var result = Result.Failure<int>(new MetadataError(ErrorMetadata.Create("value", float.NaN)));

        // Act
        var json = JsonSerializer.Serialize(result, MetadataOptions);

        // Assert
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("error").GetProperty("metadata").GetProperty("value").GetString().ShouldBe("NaN");
    }

    [Fact]
    public void Write_WhenMetadataValueIsFormattable_UsesInvariantCulture()
    {
        // Arrange
        var result = Result.Failure<int>(new MetadataError(ErrorMetadata.Create("day", new DateOnly(2026, 11, 7))));
        var previous = CultureInfo.CurrentCulture;
        string json;

        // Act
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            json = JsonSerializer.Serialize(result, MetadataOptions);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // Assert
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("error").GetProperty("metadata").GetProperty("day").GetString().ShouldBe("11/07/2026");
    }

    [Fact]
    public void RoundTrip_WhenMetadataValueIsJsonElement_KeepsItAsJsonElement()
    {
        // Arrange
        using var source = JsonDocument.Parse("""{"limits":[1,2,{"deep":true}],"name":"x"}""");
        var nested = source.RootElement.Clone();
        var json = JsonSerializer.Serialize(
            Result.Failure<int>(new MetadataError(ErrorMetadata.Create("nested", nested))), MetadataOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result<int>>(json, MetadataOptions);

        // Assert
        read.IsFailure.ShouldBeTrue();
        var element = read.Error.Metadata["nested"].ShouldBeOfType<JsonElement>();
        element.ValueKind.ShouldBe(JsonValueKind.Object);
        element.GetRawText().ShouldBe("""{"limits":[1,2,{"deep":true}],"name":"x"}""");
    }

    [Fact]
    public void Read_WhenMetadataValueIsArray_ReturnsJsonElement()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"A.B","message":"m","metadata":{"ids":[1,2]}}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions);

        // Assert
        result.IsFailure.ShouldBeTrue();
        var element = result.Error.Metadata["ids"].ShouldBeOfType<JsonElement>();
        element.ValueKind.ShouldBe(JsonValueKind.Array);
        element.GetRawText().ShouldBe("[1,2]");
    }

    [Fact]
    public void RoundTrip_WhenConverterCreatedDirectlyWithMetadata_KeepsMetadata()
    {
        // Arrange
        var json = JsonSerializer.Serialize(Result.Failure<int>(new CriticalTestError()), MetadataOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result<int>>(json, MetadataOptions);

        // Assert
        read.IsFailure.ShouldBeTrue();
        read.Error.Metadata.ShouldBe(new Dictionary<string, object?> { ["retryAfter"] = 30L });
    }

    // ── Default factory never writes metadata ─────────────────────────────

    [Fact]
    public void Write_WhenDefaultFactory_NeverWritesMetadata()
    {
        // Arrange
        var inner = new MetadataError(ErrorMetadata.Create("secret", "internal"));
        var result = Result.Failure<int>(ErrorCollection.From(inner, new CriticalTestError()));

        // Act
        var json = JsonSerializer.Serialize(result, DefaultFactoryOptions);

        // Assert
        json.ShouldNotContain("\"metadata\"", Case.Sensitive);
        json.ShouldNotContain("secret", Case.Sensitive);
        json.ShouldNotContain("retryAfter", Case.Sensitive);
    }

    [Fact]
    public void Write_WhenDefaultConverterOfT_NeverWritesMetadata()
    {
        // Arrange
        var result = Result.Failure<int>(new CriticalTestError());

        // Act
        var json = JsonSerializer.Serialize(result, DefaultConverterOfTOptions);

        // Assert: #195, a default converter writes a fault of the system with the generic message
        json.ShouldBe("""{"isSuccess":false,"error":{"code":"TEST.CRITICAL","message":"The server could not complete the request.","category":"Unavailable","severity":"Critical"}}""");
    }

    [Fact]
    public void Read_WhenDefaultFactoryAndPayloadHasMetadata_StillReadsIt()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"A.B","message":"m","metadata":{"retryAfter":30}}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Metadata.ShouldBe(new Dictionary<string, object?> { ["retryAfter"] = 30L });
    }

    // ── ErrorCollection and validation errors ─────────────────────────────

    [Fact]
    public void RoundTrip_WhenNestedCollectionWithValidationError_KeepsStructurePropertyNameAndOrder()
    {
        // Arrange
        var nested = ErrorCollection.From(ValidationError.Required("Email"), ValidationError.MaxLength("Name", 10));
        var original = Result.Failure<int>(ErrorCollection.From(nested, new CriticalTestError(), new NotFoundTestError(3)));
        var json = JsonSerializer.Serialize(original, FactoryMetadataOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result<int>>(json, FactoryMetadataOptions);

        // Assert
        read.IsFailure.ShouldBeTrue();
        var outer = read.Error.ShouldBeOfType<ErrorCollection>();
        outer.Count.ShouldBe(3);
        outer.Message.ShouldBe(original.Error.Message);
        outer.Category.ShouldBe(original.Error.Category);
        outer.Severity.ShouldBe(ErrorSeverity.Critical);

        var readNested = outer[0].ShouldBeOfType<ErrorCollection>();
        readNested.Count.ShouldBe(2);
        var email = readNested[0].ShouldBeOfType<DeserializedValidationError>();
        email.ShouldBeAssignableTo<IValidationError>();
        email.PropertyName.ShouldBe("Email");
        email.Code.Value.ShouldBe("VALIDATION.EMAIL");
        email.Category.ShouldBe(ErrorCategory.Validation);
        email.Severity.ShouldBe(ErrorSeverity.Warning);
        var name = readNested[1].ShouldBeOfType<DeserializedValidationError>();
        name.PropertyName.ShouldBe("Name");

        var critical = outer[1].ShouldBeOfType<DeserializedError>();
        critical.Code.Value.ShouldBe("TEST.CRITICAL");
        critical.Severity.ShouldBe(ErrorSeverity.Critical);
        critical.Metadata.ShouldBe(new Dictionary<string, object?> { ["retryAfter"] = 30L });

        var notFound = outer[2].ShouldBeOfType<DeserializedError>();
        notFound.Code.Value.ShouldBe("TEST.NOT_FOUND");
        notFound.Category.ShouldBe(ErrorCategory.NotFound);
    }

    [Fact]
    public void Write_WhenCollectionContainsValidationError_WritesPropertyNameAfterSeverity()
    {
        // Arrange
        var result = Result.Failure<int>(ErrorCollection.From(ValidationError.Custom("Age", "Too young")));

        // Act
        var json = JsonSerializer.Serialize(result, DefaultFactoryOptions);

        // Assert
        json.ShouldBe(
            """{"isSuccess":false,"error":{"code":"MULTI.ERROR","message":"Too young","category":"Validation","severity":"Warning","errors":[{"code":"VALIDATION.AGE","message":"Too young","category":"Validation","severity":"Warning","propertyName":"Age"}]}}""");
    }

    [Fact]
    public void Read_WhenErrorsArrayEmpty_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"MULTI.ERROR","message":"m","errors":[]}}""";

        // Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions));
    }

    // ── Read defaults and rejections ──────────────────────────────────────

    [Fact]
    public void Read_WhenMessageCategoryAndSeverityMissing_UsesDefaults()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"A.B"}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions);

        // Assert
        result.IsFailure.ShouldBeTrue();
        var error = result.Error.ShouldBeOfType<DeserializedError>();
        error.Code.Value.ShouldBe("A.B");
        error.Message.ShouldBe("Unknown error");
        error.Category.ShouldBe(ErrorCategory.Technical);
        error.Severity.ShouldBe(ErrorSeverity.Error);
        error.Metadata.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""{"isSuccess":false,"error":{}}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":null,"message":"m"}}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"","message":"m"}}""")]
    [InlineData("""{"isSuccess":false,"error":{"message":"m","errors":[{"message":"inner"}]}}""")]
    public void Read_WhenCodeIsMissingNullOrEmpty_ThrowsJsonException(string json)
    {
        // Act + Assert: an error without a code does not describe a valid result
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions));
    }

    [Fact]
    public void Read_WhenMetadataHasADuplicateKey_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"A.B","message":"m","metadata":{"tenant":"a","tenant":"b"}}}""";

        // Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions));
    }

    [Theory]
    [InlineData("NoSuchCategory")]
    [InlineData("999")]
    public void Read_WhenCategoryUnknownOrUndefined_UsesTechnical(string category)
    {
        // Arrange
        var json = $$$"""{"isSuccess":false,"error":{"code":"A.B","message":"m","category":"{{{category}}}"}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Category.ShouldBe(ErrorCategory.Technical);
    }

    [Theory]
    [InlineData("NoSuchSeverity", ErrorSeverity.Error)]
    [InlineData("42", ErrorSeverity.Error)]
    [InlineData("warning", ErrorSeverity.Warning)]
    [InlineData("INFORMATION", ErrorSeverity.Information)]
    public void Read_WhenSeverityGiven_ParsesCaseInsensitivelyOrUsesError(string severity, ErrorSeverity expected)
    {
        // Arrange
        var json = $$$"""{"isSuccess":false,"error":{"code":"A.B","message":"m","severity":"{{{severity}}}"}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Severity.ShouldBe(expected);
    }

    [Fact]
    public void RoundTrip_WhenCategoryIsUndefinedValue_ReadsTechnical()
    {
        // Arrange
        var json = JsonSerializer.Serialize(Result.Failure<int>(new UndefinedCategoryError()), DefaultFactoryOptions);

        // Act
        var read = JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions);

        // Assert
        json.ShouldContain("\"category\":\"999\"");
        read.IsFailure.ShouldBeTrue();
        read.Error.Category.ShouldBe(ErrorCategory.Technical);
    }

    [Theory]
    [InlineData("""{"isSuccess":false,"error":{"code":"","message":"m"}}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":5,"message":"m"}}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"A.B","message":"m","errors":{}}}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"A.B","message":"m","errors":["x"]}}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"A.B","message":"m","metadata":[]}}""")]
    [InlineData("""{"isSuccess":false,"error":{"code":"A.B","message":"m","metadata":5}}""")]
    public void Read_WhenErrorObjectIsInvalid_ThrowsJsonException(string json)
    {
        // Arrange + Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, DefaultFactoryOptions));
    }

    // ── Metadata access on a deserialized error ───────────────────────────

    [Fact]
    public void HasMetadata_WhenDeserializedErrorCarriesKey_ReturnsTrue()
    {
        // Arrange
        var read = ReadMetadataError(ErrorMetadata.Create("correlationId", "abc-123"));

        // Act
        var hasKey = read.Error.HasMetadata("correlationId");
        var hasOther = read.Error.HasMetadata("missing");

        // Assert
        hasKey.ShouldBeTrue();
        hasOther.ShouldBeFalse();
    }

    [Fact]
    public void TryGetMetadata_WhenDeserializedErrorCarriesString_ReturnsIt()
    {
        // Arrange
        var read = ReadMetadataError(ErrorMetadata.Create(new("correlationId", "abc-123"), new("attempt", 3)));

        // Act
        var correlationId = read.Error.TryGetMetadata<string>("correlationId");
        var attemptAsInt = read.Error.TryGetMetadata<int>("attempt");
        var attemptAsLong = read.Error.TryGetMetadata<long>("attempt");

        // Assert
        correlationId.ShouldBe("abc-123");
        attemptAsInt.ShouldBe(0);
        attemptAsLong.ShouldBe(3L);
    }

    private static Result<int> ReadMetadataError(IReadOnlyDictionary<string, object?> metadata)
    {
        var json = JsonSerializer.Serialize(Result.Failure<int>(new MetadataError(metadata)), MetadataOptions);
        var read = JsonSerializer.Deserialize<Result<int>>(json, MetadataOptions);
        read.IsFailure.ShouldBeTrue();
        read.Error.ShouldBeOfType<DeserializedError>();
        return read;
    }

    private sealed record MetadataError(IReadOnlyDictionary<string, object?> Values)
        : Error(ErrorCode.From("TEST.METADATA"), "Metadata error")
    {
        public override IReadOnlyDictionary<string, object?> Metadata => Values;
    }

    private sealed record UndefinedCategoryError()
        : Error(ErrorCode.From("TEST.UNDEFINED"), "Undefined category")
    {
        public override ErrorCategory Category => (ErrorCategory)999;
    }

    private sealed class Money(int amount, string currency)
    {
        public override string ToString() => $"{amount} {currency}";
    }

    [Fact]
    public void Read_WhenErrorObjectHasDuplicateProperty_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"A.B","message":"m","category":"Validation","category":"Technical"}}""";

        // Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, DuplicateOptions));
    }

    [Fact]
    public void Read_WhenMetadataPresent_ReturnsImmutableMetadata()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"A.B","message":"m","metadata":{"k":1}}}""";

        // Act
        Result<int> result = JsonSerializer.Deserialize<Result<int>>(json, DuplicateOptions);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Metadata.ShouldBeAssignableTo<System.Collections.Immutable.IImmutableDictionary<string, object?>>();
    }

    private static readonly JsonSerializerOptions DuplicateOptions = new() { Converters = { new MicroKit.Result.Serialization.ResultJsonConverterFactory() } };
}
