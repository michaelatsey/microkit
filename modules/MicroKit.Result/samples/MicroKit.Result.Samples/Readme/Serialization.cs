using MicroKit.Result.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Result.Samples.Readme;

// Verbatim copy of the README "Serialization" block — keep it identical to README.md.
internal static class SerializationSamples
{
    internal static void SerializationSample(WebApplicationBuilder builder)
    {
// Register globally
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new ResultJsonConverterFactory()));

// Wire-format (success)
// {"isSuccess":true,"value":{...}}

// Wire-format (failure)
// {"isSuccess":false,"error":{"code":"USER.NOT_FOUND","message":"...","category":"NotFound"}}
    }
}
