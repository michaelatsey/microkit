namespace MicroKit.Result;

/// <summary>
/// Fluent builder for constructing immutable error metadata dictionaries.
/// </summary>
/// <example>
/// <code>
/// IReadOnlyDictionary&lt;string, object?&gt; meta = new ErrorMetadataBuilder()
///     .WithTimestamp()
///     .WithCorrelationId(correlationId)
///     .WithTraceId()
///     .Add("userId", userId)
///     .Build();
/// </code>
/// </example>
public sealed class ErrorMetadataBuilder
{
    private readonly ImmutableDictionary<string, object?>.Builder _builder =
        ImmutableDictionary.CreateBuilder<string, object?>();

    /// <summary>
    /// Adds a <c>timestamp</c> entry set to the current UTC time.
    /// </summary>
    /// <returns>This builder instance.</returns>
    public ErrorMetadataBuilder WithTimestamp()
    {
        _builder["timestamp"] = DateTimeOffset.UtcNow;
        return this;
    }

    /// <summary>
    /// Adds a <c>correlationId</c> entry with the specified value.
    /// </summary>
    /// <param name="correlationId">The correlation identifier, or <see langword="null"/> to store an explicit null.</param>
    /// <returns>This builder instance.</returns>
    public ErrorMetadataBuilder WithCorrelationId(string? correlationId)
    {
        _builder["correlationId"] = correlationId;
        return this;
    }

    /// <summary>
    /// Adds a <c>traceId</c> entry from <see cref="Activity.Current"/>, if an active trace exists.
    /// </summary>
    /// <returns>This builder instance.</returns>
    public ErrorMetadataBuilder WithTraceId()
    {
        _builder["traceId"] = Activity.Current?.TraceId.ToString();
        return this;
    }

    /// <summary>
    /// Adds or overwrites a single key-value pair.
    /// </summary>
    /// <param name="key">The metadata key.</param>
    /// <param name="value">The metadata value.</param>
    /// <returns>This builder instance.</returns>
    public ErrorMetadataBuilder Add(string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        _builder[key] = value;
        return this;
    }

    /// <summary>
    /// Merges all pairs from <paramref name="other"/> into this builder, overwriting any existing keys.
    /// </summary>
    /// <param name="other">The source dictionary to merge from.</param>
    /// <returns>This builder instance.</returns>
    public ErrorMetadataBuilder Merge(IReadOnlyDictionary<string, object?> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var pair in other)
            _builder[pair.Key] = pair.Value;
        return this;
    }

    /// <summary>
    /// Builds the immutable metadata dictionary.
    /// </summary>
    /// <returns>An <see cref="ImmutableDictionary{TKey,TValue}"/> containing all added entries.</returns>
    public ImmutableDictionary<string, object?> Build() => _builder.ToImmutable();

    /// <summary>
    /// Implicitly converts this builder to an <see cref="ImmutableDictionary{TKey,TValue}"/>.
    /// </summary>
    /// <param name="builder">The builder to convert.</param>
    public static implicit operator ImmutableDictionary<string, object?>(ErrorMetadataBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Build();
    }

}
