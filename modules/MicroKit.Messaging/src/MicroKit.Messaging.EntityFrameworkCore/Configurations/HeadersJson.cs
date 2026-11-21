using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace MicroKit.Messaging.EntityFrameworkCore;

/// <summary>
/// Converts the headers of <see cref="OutboxMessage.Headers"/> and <see cref="InboxMessage.Headers"/>
/// to and from their JSON column, trim-safely.
/// </summary>
internal static class HeadersJson
{
    /// <summary>
    /// The value comparer of a headers column. Required: without it EF compares the dictionary by
    /// reference and would never see a change to its contents. Order-independent, both ways: a
    /// dictionary read back from JSON and one built in another order are equal and hash alike.
    /// </summary>
    public static ValueComparer<IReadOnlyDictionary<string, string>?> Comparer { get; } = new(
        (a, b) => a == null ? b == null : b != null && a.Count == b.Count && !a.Except(b).Any(),
        v => v == null ? 0 : v.Aggregate(0, (h, kv) => h ^ HashCode.Combine(kv.Key, kv.Value)),
        v => v == null ? null : new Dictionary<string, string>(v, StringComparer.Ordinal));

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
