namespace MicroKit.Messaging.Serialization;

/// <summary>
/// Resolves the assembly-qualified <c>EventType</c> an outbox or inbox row records back to a CLR
/// type, for the type-based serializer.
/// </summary>
/// <remarks>
/// Before #186 this lookup lived inside the serializer (<c>Deserialize(string, string)</c>), and it
/// stays in Core with it: the rows still name their payload type by assembly-qualified name, and
/// Core is where the reflection-based row path is accepted for trimming (IL2057, see the project
/// file). #188 replaces the row path.
/// </remarks>
internal static class RowEventTypes
{
    /// <summary>The type <paramref name="eventType"/> names, or null when it does not resolve.</summary>
    /// <param name="eventType">The row's assembly-qualified type name.</param>
    /// <returns>The type, or <see langword="null"/>.</returns>
    public static Type? Resolve(string eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return Type.GetType(eventType, throwOnError: false);
    }
}
