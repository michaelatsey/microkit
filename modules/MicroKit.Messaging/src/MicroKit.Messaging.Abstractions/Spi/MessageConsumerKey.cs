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
    /// <summary>The consumer key of <paramref name="handlerType"/>.</summary>
    /// <param name="handlerType">The handler's type.</param>
    /// <returns><c>Namespace.Type, AssemblyName</c>.</returns>
    public static string Of(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        return FromAssemblyQualifiedName(handlerType.AssemblyQualifiedName
            ?? throw new ArgumentException($"'{handlerType}' has no assembly-qualified name.", nameof(handlerType)));
    }

    /// <summary>The consumer key of an assembly-qualified type name: its version parts removed.</summary>
    /// <param name="assemblyQualifiedName">An assembly-qualified type name.</param>
    /// <returns>The name without <c>Version</c>, <c>Culture</c> or <c>PublicKeyToken</c>.</returns>
    public static string FromAssemblyQualifiedName(string assemblyQualifiedName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyQualifiedName);
        return VersionParts().Replace(assemblyQualifiedName, string.Empty);
    }

    [GeneratedRegex(@", (?:Version|Culture|PublicKeyToken)=[^,\]]+", RegexOptions.CultureInvariant)]
    private static partial Regex VersionParts();
}
