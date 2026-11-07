using System.Text.Json;
using MicroKit.Result.Serialization;

namespace MicroKit.Result.UnitTests.Serialization;

// ADR-RESULT-001 D6: "value": null on a successful payload is invalid; the converter fails with JsonException,
// never with the ArgumentNullException of Success(null).
public sealed class ResultJsonConverterNullValueTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new ResultJsonConverterFactory() } };

    [Fact]
    public void Read_WhenSuccessWithNullReferenceValue_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":null}""";

        // Act + Assert
        var thrown = Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result<string>>(json, Options));
        thrown.ShouldBeOfType<JsonException>();
        thrown.InnerException.ShouldBeOfType<ArgumentNullException>();
    }

    [Fact]
    public void Read_WhenValueNullPrecedesIsSuccess_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"value":null,"isSuccess":true}""";

        // Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result<string>>(json, Options));
    }

    [Fact]
    public void Read_WhenSuccessWithNullNullableValueType_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":null}""";

        // Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result<int?>>(json, Options));
    }

    [Fact]
    public void Read_WhenSuccessWithNullObjectValue_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":null}""";

        // Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Result<Payload>>(json, Options));
    }

    [Fact]
    public void Read_WhenSuccessWithNonNullValue_ReturnsSuccess()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":"ada"}""";

        // Act
        var result = JsonSerializer.Deserialize<Result<string>>(json, Options);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("ada");
    }

    private sealed record Payload(string Name);
}
