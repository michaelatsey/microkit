using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroKit.Messaging.EntityFrameworkCore;

/// <summary>Converts <see cref="OutboxMessage.Headers"/> to and from its JSON column, trim-safely.</summary>
internal static class HeadersJson
{
    /// <summary>The headers as one JSON object, or null when there are none.</summary>
    /// <param name="headers">The headers.</param>
    /// <returns>The JSON text, or <see langword="null"/>.</returns>
    public static string? Write(IReadOnlyDictionary<string, string>? headers)
        => headers is null || headers.Count == 0
            ? null
            : JsonSerializer.Serialize(
                new Dictionary<string, string>(headers, StringComparer.Ordinal),
                HeadersJsonContext.Default.DictionaryStringString);

    /// <summary>The headers read back from their JSON column.</summary>
    /// <param name="json">The JSON text, or null.</param>
    /// <returns>The headers, or <see langword="null"/>.</returns>
    public static IReadOnlyDictionary<string, string>? Read(string? json)
        => json is null
            ? null
            : JsonSerializer.Deserialize(json, HeadersJsonContext.Default.DictionaryStringString);
}

/// <summary>The source-generated serializer of the headers column.</summary>
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class HeadersJsonContext : JsonSerializerContext;
