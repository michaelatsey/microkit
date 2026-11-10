namespace MicroKit.Messaging.Serialization;

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;

/// <summary>
/// Default <see cref="IMessageSerializer"/>: System.Text.Json, <c>application/json</c>, with the
/// host's options applied in both directions.
/// </summary>
/// <remarks>
/// <para>
/// The options come from <see cref="MessagingJsonOptions"/>, which the host configures through
/// <see cref="MessagingBuilder.ConfigureJson"/>: converters, naming, and a
/// <c>JsonSerializerContext</c> added to <c>TypeInfoResolverChain</c>. They are frozen once, at
/// construction, so the same options serialize and deserialize for the process's lifetime.
/// </para>
/// <para>
/// <b>Reflection stays the fallback.</b> A host that adds a source-generated context to the
/// resolver chain would otherwise lose every type that context does not declare. When reflection
/// is enabled, a <see cref="DefaultJsonTypeInfoResolver"/> is appended after the host's resolvers,
/// so a declared type uses the context and any other type still serializes.
/// </para>
/// <para>
/// A malformed body throws <see cref="JsonException"/>; callers classify it as permanent and keep
/// the exception as the reason.
/// </para>
/// </remarks>
internal sealed class SystemTextJsonMessageSerializer : IMessageSerializer
{
    /// <summary>The media type this serializer produces.</summary>
    public const string JsonContentType = "application/json";

    private readonly JsonSerializerOptions _options;

    /// <summary>Creates the serializer over the host's configured options.</summary>
    /// <param name="options">The messaging JSON options.</param>
    public SystemTextJsonMessageSerializer(IOptions<MessagingJsonOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = Freeze(options.Value.SerializerOptions);
    }

    /// <summary>Creates the serializer over the default options. For tests and fixtures.</summary>
    internal SystemTextJsonMessageSerializer()
        : this(Microsoft.Extensions.Options.Options.Create(new MessagingJsonOptions()))
    {
    }

    /// <inheritdoc />
    public string ContentType => JsonContentType;

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Serialize(object message, Type type)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(type);
        return JsonSerializer.SerializeToUtf8Bytes(message, type, _options);
    }

    /// <inheritdoc />
    public object? Deserialize(ReadOnlyMemory<byte> body, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return JsonSerializer.Deserialize(body.Span, type, _options);
    }

    /// <summary>
    /// Whether <paramref name="contentType"/> is <c>application/json</c>, ignoring case and any
    /// media-type parameters such as <c>charset</c>.
    /// </summary>
    /// <param name="contentType">The media type to test.</param>
    /// <returns><see langword="true"/> for a JSON media type.</returns>
    internal static bool IsJson(string? contentType)
    {
        if (contentType is null)
        {
            return false;
        }

        var mediaType = contentType.AsSpan();
        var parameters = mediaType.IndexOf(';');
        if (parameters >= 0)
        {
            mediaType = mediaType[..parameters];
        }

        return mediaType.Trim().Equals(JsonContentType, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonSerializerOptions Freeze(JsonSerializerOptions options)
    {
        if (options.IsReadOnly)
        {
            return options;
        }

        if (JsonSerializer.IsReflectionEnabledByDefault
            && options.TypeInfoResolverChain.Count > 0
            && !options.TypeInfoResolverChain.OfType<DefaultJsonTypeInfoResolver>().Any())
        {
            options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        }

        options.MakeReadOnly(populateMissingResolver: JsonSerializer.IsReflectionEnabledByDefault);
        return options;
    }
}
