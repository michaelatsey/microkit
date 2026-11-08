using System.Text.Json;
using System.Text.Json.Serialization;
using MicroKit.Result.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Result.Samples.Guide;

// Backs docs/guides/usage.md, "Serialize results to JSON": its C# blocks are here verbatim, modulo indentation; the
// rest only makes them build. Keep the two identical.
internal static class SerializationSamples
{
    internal static void RegistrationSample(WebApplicationBuilder builder)
    {
        // Minimal APIs
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new ResultJsonConverterFactory()));

        // MVC controllers, which do not read the Minimal API options
        builder.Services.AddControllers().AddJsonOptions(o =>
            o.JsonSerializerOptions.Converters.Add(new ResultJsonConverterFactory()));

        // An in-process store such as a cache, never a response: the Full view keeps every error, message and Metadata
        var cacheOptions = new JsonSerializerOptions();
        cacheOptions.Converters.Add(new ResultJsonConverterFactory(ResultJsonView.Full));
    }

    internal static void RoundTripSample(JsonSerializerOptions options, Guid orderId)
    {
        Result<Order> failed = Error.NotFound(ErrorCode.From("ORDER.NOT_FOUND"), $"Order {orderId} does not exist.");
        string json = JsonSerializer.Serialize(failed, options);
        Result<Order> restored = JsonSerializer.Deserialize<Result<Order>>(json, options);

        // The non-generic Result carries its own converter: no registration needed
        string command = JsonSerializer.Serialize(Result.Success()); // {"isSuccess":true}
    }

    internal static void NativeAotSample(WebApplicationBuilder builder, Result<OrderDto> order)
    {
        string json = JsonSerializer.Serialize(order, AppJsonContext.Default.ResultOrderDto);
        Result<OrderDto> restored = JsonSerializer.Deserialize(json, AppJsonContext.Default.ResultOrderDto);

        // Minimal APIs: the context resolves the contracts of the endpoints; its Converters apply to its own options only,
        // so the host's options register the closed converter too
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
            o.SerializerOptions.Converters.Add(new ResultJsonConverter<OrderDto>());
        });
    }

    internal sealed record Order(Guid Id);
}

internal sealed record OrderDto(Guid Id, decimal Total);

[JsonSourceGenerationOptions(Converters = [typeof(ResultJsonConverter<OrderDto>)])]
[JsonSerializable(typeof(Result<OrderDto>))]
[JsonSerializable(typeof(OrderDto))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
