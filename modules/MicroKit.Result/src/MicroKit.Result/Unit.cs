namespace MicroKit.Result;

/// <summary>
/// Represents the absence of a value. Use as a void-safe type parameter in generic contexts
/// where <see langword="void"/> is not permitted.
/// </summary>
/// <example>
/// <code>
/// Result&lt;Unit&gt; result = Result.Success(Unit.Value);
/// </code>
/// </example>
public readonly record struct Unit
{
    /// <summary>
    /// Gets the singleton Unit value.
    /// </summary>
    public static readonly Unit Value;

    /// <summary>
    /// Returns the string representation of Unit.
    /// </summary>
    public override string ToString() => "()";
}
