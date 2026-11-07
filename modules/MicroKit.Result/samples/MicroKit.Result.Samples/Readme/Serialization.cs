using MicroKit.Result.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Result.Samples.Readme;

// README "Serialization": its C# blocks are here verbatim, modulo indentation; the rest only makes them build. Keep them identical to README.md.
internal static class SerializationSamples
{
    internal static void SerializationSample(WebApplicationBuilder builder)
    {
        // Register for Minimal APIs
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new ResultJsonConverterFactory()));

        // Register for MVC controllers, which do not read the Minimal API options
        builder.Services.AddControllers().AddJsonOptions(o =>
            o.JsonSerializerOptions.Converters.Add(new ResultJsonConverterFactory()));

        // For an in-process store such as a cache, never for a response: also write Metadata
        var cacheOptions = new System.Text.Json.JsonSerializerOptions();
        cacheOptions.Converters.Add(new ResultJsonConverterFactory(includeMetadata: true));

        // Wire-format (success)
        // {"isSuccess":true,"value":{...}}

        // Wire-format (failure)
        // {"isSuccess":false,"error":{"code":"USER.NOT_FOUND","message":"...","category":"NotFound","severity":"Error"}}
    }
}
