namespace MicroKit.Messaging.Bus;

using System.Reflection;

/// <summary>Reads <see cref="MessageContractAttribute"/> off a local type.</summary>
internal static class MessageContracts
{
    /// <summary>The attribute <paramref name="type"/> declares, or null.</summary>
    /// <param name="type">The local event type.</param>
    /// <returns>The attribute, or <see langword="null"/> when the type carries none.</returns>
    public static MessageContractAttribute? Find(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.GetCustomAttribute<MessageContractAttribute>(inherit: false);
    }
}
