namespace MicroKit.Messaging.Serialization;

using System.Text.Json;

/// <summary>
/// Default <see cref="IMessageSerializer"/> implementation backed by
/// <c>System.Text.Json</c>. Uses reflection-based serialization via
/// <c>evt.GetType()</c> so that concrete subtype properties are preserved
/// when the event is referenced through <see cref="IIntegrationEvent"/>.
/// </summary>
/// <remarks>
/// IL2026/IL2057 suppressions are intentional — reflection-based JSON is the
/// v1 approach. AOT/trim support via source-generator type registry is planned for v2
/// (<c>MicroKit.Messaging.Serialization</c>).
/// </remarks>
internal sealed class SystemTextJsonMessageSerializer : IMessageSerializer
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public string Serialize(IIntegrationEvent evt)
        => JsonSerializer.Serialize(evt, evt.GetType(), _options);

    /// <inheritdoc />
    public IIntegrationEvent? Deserialize(string payload, string eventType)
    {
        var type = Type.GetType(eventType);
        if (type is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize(payload, type, _options) as IIntegrationEvent;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
