using System.Text.Json;
using MicroKit.Result.Serialization;
using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Serialization;

public sealed class ResultJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new ResultJsonConverterFactory() },
    };

    // ── Write ─────────────────────────────────────────────────────────────

    [Fact]
    public void Write_WhenSuccess_WritesIsSuccessAndValue()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var json = JsonSerializer.Serialize(result, Options);

        // Assert
        json.ShouldBe("""{"isSuccess":true,"value":42}""");
    }

    [Fact]
    public void Write_WhenSuccessWithRecordValue_WritesValueObject()
    {
        // Arrange
        var result = Result.Success(new Person("Ada", 36));

        // Act
        var json = JsonSerializer.Serialize(result, Options);

        // Assert
        json.ShouldBe("""{"isSuccess":true,"value":{"Name":"Ada","Age":36}}""");
    }

    [Fact]
    public void Write_WhenFailure_WritesCodeMessageAndCategory()
    {
        // Arrange
        var result = Result.Failure<int>(new NotFoundTestError(7));

        // Act
        var json = JsonSerializer.Serialize(result, Options);

        // Assert
        json.ShouldBe("""{"isSuccess":false,"error":{"code":"TEST.NOT_FOUND","message":"Item 7 not found","category":"NotFound"}}""");
    }

    [Fact]
    public void Write_WhenFailureHasSeverityAndMetadata_WritesNeither()
    {
        // Arrange
        var result = Result.Failure<int>(new CriticalTestError());

        // Act
        var json = JsonSerializer.Serialize(result, Options);

        // Assert
        using var document = JsonDocument.Parse(json);
        var error = document.RootElement.GetProperty("error");
        error.EnumerateObject().Select(p => p.Name).ShouldBe(["code", "message", "category"]);
        error.GetProperty("category").GetString().ShouldBe("Unavailable");
    }

    [Fact]
    public void Write_WhenFailureIsErrorCollection_WritesAggregateWithoutInnerErrors()
    {
        // Arrange
        var result = Result.Failure<int>(ErrorCollection.From(new NotFoundTestError(1), new OtherError()));

        // Act
        var json = JsonSerializer.Serialize(result, Options);

        // Assert
        json.ShouldBe("""{"isSuccess":false,"error":{"code":"MULTI.ERROR","message":"Item 1 not found; Other error","category":"NotFound"}}""");
    }

    [Fact]
    public void Write_WhenDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> result = default;

        // Act + Assert
        Should.Throw<ResultException>(() => JsonSerializer.Serialize(result, Options));
    }

    [Fact]
    public void Write_WhenNestedInCollection_WritesEachResult()
    {
        // Arrange
        Result<int>[] results = [Result.Success(1), Result.Failure<int>(new OtherError())];

        // Act
        var json = JsonSerializer.Serialize(results, Options);

        // Assert
        json.ShouldBe("""[{"isSuccess":true,"value":1},{"isSuccess":false,"error":{"code":"TEST.OTHER","message":"Other error","category":"Conflict"}}]""");
    }

    // ── Round trip ────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_WhenSuccessWithRecordValue_KeepsValue()
    {
        // Arrange
        var original = Result.Success(new Person("Ada", 36));
        var json = JsonSerializer.Serialize(original, Options);

        // Act
        var read = JsonSerializer.Deserialize<Result<Person>>(json, Options);

        // Assert
        read.IsSuccess.ShouldBeTrue();
        read.Value.ShouldBe(new Person("Ada", 36));
    }

    [Fact]
    public void RoundTrip_WhenFailure_KeepsCodeMessageAndCategory()
    {
        // Arrange
        var original = Result.Failure<int>(new NotFoundTestError(7));
        var json = JsonSerializer.Serialize(original, Options);

        // Act
        var read = JsonSerializer.Deserialize<Result<int>>(json, Options);

        // Assert
        read.IsFailure.ShouldBeTrue();
        read.Error.Code.Value.ShouldBe("TEST.NOT_FOUND");
        read.Error.Message.ShouldBe("Item 7 not found");
        read.Error.Category.ShouldBe(ErrorCategory.NotFound);
    }

    [Fact]
    public void RoundTrip_WhenFailureHasSeverityAndMetadata_LosesThem()
    {
        // Arrange
        var original = Result.Failure<int>(new CriticalTestError());
        var json = JsonSerializer.Serialize(original, Options);

        // Act
        var read = JsonSerializer.Deserialize<Result<int>>(json, Options);

        // Assert
        read.IsFailure.ShouldBeTrue();
        read.Error.Category.ShouldBe(ErrorCategory.Unavailable);
        read.Error.Severity.ShouldBe(ErrorSeverity.Error);
        read.Error.Metadata.ShouldBeEmpty();
    }

    // ── Read ──────────────────────────────────────────────────────────────

    [Fact]
    public void Read_WhenSuccess_ReturnsSuccessWithValue()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":42}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, Options);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Read_WhenValueBeforeIsSuccess_ReturnsSuccessWithValue()
    {
        // Arrange
        const string json = """{"value":42,"isSuccess":true}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, Options);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Read_WhenFailure_ReturnsErrorOfPrivateTypeWithCodeMessageAndCategory()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"TEST.NOT_FOUND","message":"Item 7 not found","category":"NotFound"}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, Options);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldNotBeOfType<NotFoundTestError>();
        result.Error.GetType().IsNestedPrivate.ShouldBeTrue();
        result.Error.Code.Value.ShouldBe("TEST.NOT_FOUND");
        result.Error.Message.ShouldBe("Item 7 not found");
        result.Error.Category.ShouldBe(ErrorCategory.NotFound);
        result.Error.Severity.ShouldBe(ErrorSeverity.Error);
        result.Error.Metadata.ShouldBeEmpty();
    }

    [Fact]
    public void Read_WhenUnknownPropertiesPresent_SkipsThem()
    {
        // Arrange
        const string json = """
            {
              "extra": {"nested": [1, 2, {"deep": true}]},
              "isSuccess": false,
              "error": {
                "severity": "Critical",
                "code": "TEST.OTHER",
                "metadata": {"retryAfter": 30},
                "message": "Other error",
                "category": "Conflict",
                "errors": [{"code": "X"}]
              },
              "trailing": "ignored"
            }
            """;

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, Options);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.Value.ShouldBe("TEST.OTHER");
        result.Error.Message.ShouldBe("Other error");
        result.Error.Category.ShouldBe(ErrorCategory.Conflict);
        result.Error.Severity.ShouldBe(ErrorSeverity.Error);
        result.Error.Metadata.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("notfound", ErrorCategory.NotFound)]
    [InlineData("NOTFOUND", ErrorCategory.NotFound)]
    [InlineData("businessRule", ErrorCategory.BusinessRule)]
    public void Read_WhenCategoryCaseDiffers_ParsesCaseInsensitively(string category, ErrorCategory expected)
    {
        // Arrange
        var json = $$$"""{"isSuccess":false,"error":{"code":"X.Y","message":"m","category":"{{{category}}}"}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, Options);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Category.ShouldBe(expected);
    }

    [Fact]
    public void Read_WhenCategoryUnknown_UsesTechnical()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":{"code":"X.Y","message":"m","category":"NoSuchCategory"}}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<int>>(json, Options);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Category.ShouldBe(ErrorCategory.Technical);
    }

    [Fact]
    public void Read_WhenIsSuccessMissing_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"value":42}""";

        // Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, Options));
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
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, Options));
    }

    [Theory]
    [InlineData("""{"isSuccess":[true]}""")]
    [InlineData("""{"isSuccess":{"a":1}}""")]
    [InlineData("""{"isSuccess":"true"}""")]
    public void Read_WhenIsSuccessIsNotBoolean_ThrowsJsonException(string json)
    {
        // Arrange + Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, Options));
    }

    [Fact]
    public void Read_WhenErrorIsNotObject_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":false,"error":"boom"}""";

        // Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, Options));
    }

    [Fact]
    public void Read_WhenValueHasWrongType_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":"not a number"}""";

        // Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, Options));
    }

    [Fact]
    public void Read_WhenSuccessHasNoValue_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":true}""";

        // Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, Options));
    }

    [Fact]
    public void Read_WhenSuccessAlsoHasError_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":1,"error":{"code":"TEST.OTHER","message":"Other error","category":"Conflict"}}""";

        // Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, Options));
    }

    [Fact]
    public void Read_WhenFailureHasNoError_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":false}""";

        // Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, Options));
    }

    // ── Factory ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(typeof(Result<int>), true)]
    [InlineData(typeof(Result<string>), true)]
    [InlineData(typeof(Result), false)]
    [InlineData(typeof(int), false)]
    [InlineData(typeof(List<int>), false)]
    public void CanConvert_WhenTypeGiven_AcceptsOnlyResultOfT(Type type, bool expected)
    {
        // Arrange
        var factory = new ResultJsonConverterFactory();

        // Act
        var canConvert = factory.CanConvert(type);

        // Assert
        canConvert.ShouldBe(expected);
    }

    [Fact]
    public void CreateConverter_WhenResultOfT_ReturnsTypedConverter()
    {
        // Arrange
        var factory = new ResultJsonConverterFactory();

        // Act
        var converter = factory.CreateConverter(typeof(Result<int>), Options);

        // Assert
        converter.ShouldBeOfType<ResultJsonConverter<int>>();
    }

    private sealed record Person(string Name, int Age);
}
