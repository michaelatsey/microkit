using System.Text.RegularExpressions;

namespace MicroKit.Messaging.Spi;

/// <summary>
/// The inbox key of a consumer — the <c>ConsumerType</c> of its inbox rows — built from its handler
/// type, free of the assembly's version, culture and public key token (#188).
/// </summary>
/// <remarks>
/// <para>
/// <c>Namespace.Type, AssemblyName</c>: an <see cref="Type.AssemblyQualifiedName"/> would change with
/// every assembly-version bump, so a delivery redelivered across a deployment would find no row under
/// the new key and run its handler again, and a <c>Failed</c> row would dead-letter as an unknown
/// consumer. A generic handler's type arguments are stripped the same way.
/// </para>
/// <para>
/// Bus handlers and the MediatR glue's notification handlers are keyed by this one function. An inbox
/// written with assembly-qualified keys is converted by the transformation
/// <see cref="FromAssemblyQualifiedName"/> applies; the CHANGELOG gives it as SQL.
/// </para>
/// </remarks>
public static partial class MessageConsumerKey
{
    /// <summary>
    /// The character no key built by <see cref="Of"/>, and no <see cref="LocalConsumer.ConsumerType"/>,
    /// contains: the inbox claim joins a consumer key and a tenant id with it (#210), so that the joined
    /// value names exactly one (consumer, tenant) pair. <see cref="FromAssemblyQualifiedName"/>, a
    /// migration helper over stored names, does not check it.
    /// </summary>
    internal const char Separator = '\u001F';

    /// <summary>The consumer key of <paramref name="handlerType"/>.</summary>
    /// <param name="handlerType">The handler's type.</param>
    /// <returns><c>Namespace.Type, AssemblyName</c>.</returns>
    /// <exception cref="ArgumentException">
    /// The type has no assembly-qualified name, or its key contains U+001F (unit separator): the inbox
    /// claim joins a consumer key and a tenant id with that character (#210), so such a key could match
    /// another pair. No C# type name contains it: a key gets one only from an assembly name. A bus handler whose key does fails when it is
    /// registered; a consumer whose key a source computes — the MediatR glue's notification handlers —
    /// fails when the source is read, at host start.
    /// </exception>
    public static string Of(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        var key = FromAssemblyQualifiedName(handlerType.AssemblyQualifiedName
            ?? throw new ArgumentException($"'{handlerType}' has no assembly-qualified name.", nameof(handlerType)));
        return key.Contains(Separator, StringComparison.Ordinal)
            ? throw new ArgumentException(
                $"The consumer key of '{Visible(handlerType.ToString())}' contains U+001F (unit separator, shown as " +
                "<U+001F>), which the inbox claim uses to join a consumer key and a tenant id. Rename the type or " +
                "its assembly.",
                nameof(handlerType))
            : key;
    }

    /// <summary>The consumer key of an assembly-qualified type name: its version parts removed.</summary>
    /// <param name="assemblyQualifiedName">An assembly-qualified type name.</param>
    /// <returns>The name without <c>Version</c>, <c>Culture</c> or <c>PublicKeyToken</c>.</returns>
    public static string FromAssemblyQualifiedName(string assemblyQualifiedName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyQualifiedName);
        return VersionParts().Replace(assemblyQualifiedName, string.Empty);
    }

    /// <summary>A name as a log shows it: the invisible separator spelled out.</summary>
    private static string Visible(string name) => name.Replace(Separator.ToString(), "<U+001F>", StringComparison.Ordinal);

    [GeneratedRegex(@", (?:Version|Culture|PublicKeyToken)=[^,\]]+", RegexOptions.CultureInvariant)]
    private static partial Regex VersionParts();
}
