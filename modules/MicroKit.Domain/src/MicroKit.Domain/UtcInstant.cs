namespace MicroKit.Domain;

/// <summary>
/// The single rule applied to every instant entering the domain: it is stored at offset zero
/// and is never <c>default(DateTimeOffset)</c>.
/// </summary>
internal static class UtcInstant
{
    /// <summary>
    /// Returns <paramref name="value"/> at offset zero, preserving the instant.
    /// </summary>
    /// <param name="value">The instant to normalise.</param>
    /// <param name="paramName">The name of the parameter or property the instant was supplied as.</param>
    /// <returns>The same instant, with <see cref="DateTimeOffset.Offset"/> equal to zero.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="value"/> is <c>default(DateTimeOffset)</c>, which signals an
    /// instant that was never set rather than a real one.
    /// </exception>
    internal static DateTimeOffset Normalize(DateTimeOffset value, string paramName)
    {
        if (value == default)
            throw new ArgumentOutOfRangeException(
                paramName, value, "An instant cannot be default(DateTimeOffset).");

        return value.ToUniversalTime();
    }
}
