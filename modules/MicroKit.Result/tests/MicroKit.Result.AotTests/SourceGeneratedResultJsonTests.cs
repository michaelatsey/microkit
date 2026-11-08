using System.Text.Json;
using System.Text.Json.Serialization;
using MicroKit.Result.Serialization;

namespace MicroKit.Result.AotTests;

public sealed record OrderDto(int Id, string Name);

// The converter registered at compile time: the generated code instantiates ResultJsonConverter<OrderDto>, so a
// [RequiresUnreferencedCode] / [RequiresDynamicCode] on it fails this project's Release build (IL2026 / IL3050).
[JsonSourceGenerationOptions(Converters = [typeof(ResultJsonConverter<OrderDto>)])]
[JsonSerializable(typeof(Result<OrderDto>))]
[JsonSerializable(typeof(OrderDto))]
internal sealed partial class RegisteredJsonContext : JsonSerializerContext;

// No converter registered: Result<T> writes the envelope by its own attribute (#195 item 6), reading needs the converter
[JsonSerializable(typeof(Result<OrderDto>))]
[JsonSerializable(typeof(OrderDto))]
internal sealed partial class UnregisteredJsonContext : JsonSerializerContext;

// #195 item 2: Result<T> serializes under Native AOT constraints, reflection-based System.Text.Json disabled. Only
// the JsonTypeInfo<T> overloads of JsonSerializer are called: the others are reflection-based (IL2026 / IL3050).
public sealed class SourceGeneratedResultJsonTests
{
    [Fact]
    public void IsReflectionEnabledByDefault_InThisProject_IsFalse()
    {
        // Act
        bool enabled = JsonSerializer.IsReflectionEnabledByDefault;

        // Assert
        enabled.ShouldBeFalse();
    }

    [Fact]
    public void RoundTrip_WhenSuccessThroughRegisteredContext_KeepsTheValue()
    {
        // Arrange
        var original = Result.Success(new OrderDto(7, "Pen"));

        // Act
        string json = JsonSerializer.Serialize(original, RegisteredJsonContext.Default.ResultOrderDto);
        Result<OrderDto> read = JsonSerializer.Deserialize(json, RegisteredJsonContext.Default.ResultOrderDto);

        // Assert
        read.IsSuccess.ShouldBeTrue();
        read.Value.ShouldBe(new OrderDto(7, "Pen"));
    }

    [Fact]
    public void RoundTrip_WhenFailureThroughRegisteredContext_KeepsTheError()
    {
        // Arrange
        var original = Result.Failure<OrderDto>(Error.NotFound(ErrorCode.From("ORDER.NOT_FOUND"), "Order 7 does not exist."));

        // Act
        string json = JsonSerializer.Serialize(original, RegisteredJsonContext.Default.ResultOrderDto);
        Result<OrderDto> read = JsonSerializer.Deserialize(json, RegisteredJsonContext.Default.ResultOrderDto);

        // Assert
        read.IsFailure.ShouldBeTrue();
        read.Error.Code.Value.ShouldBe("ORDER.NOT_FOUND");
        read.Error.Category.ShouldBe(ErrorCategory.NotFound);
        read.Error.Message.ShouldBe("Order 7 does not exist.");
    }

    [Fact]
    public void Deserialize_WhenSuccessValueIsNullThroughRegisteredContext_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":null}""";

        // Act + Assert
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize(json, RegisteredJsonContext.Default.ResultOrderDto));
    }

    [Fact]
    public void Serialize_WhenSuccessThroughUnregisteredContext_WritesTheEnvelope()
    {
        // Arrange
        var result = Result.Success(new OrderDto(7, "Pen"));

        // Act
        string json = JsonSerializer.Serialize(result, UnregisteredJsonContext.Default.ResultOrderDto);

        // Assert: the envelope's names are literal; the value follows the context's naming, PascalCase by default
        json.ShouldBe("""{"isSuccess":true,"value":{"Id":7,"Name":"Pen"}}""");
    }

    [Fact]
    public void Deserialize_ThroughUnregisteredContext_ThrowsJsonExceptionNamingTheFactory()
    {
        // Arrange
        const string json = """{"isSuccess":true,"value":{"Id":7,"Name":"Pen"}}""";

        // Act
        var exception = Record.Exception(() =>
            JsonSerializer.Deserialize(json, UnregisteredJsonContext.Default.ResultOrderDto));

        // Assert: an unreadable payload, as the module's rule has it
        exception.ShouldBeOfType<JsonException>();
        exception.Message.ShouldContain(nameof(ResultJsonConverterFactory));
    }
}
