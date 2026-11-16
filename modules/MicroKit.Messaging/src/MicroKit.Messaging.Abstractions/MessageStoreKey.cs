namespace MicroKit.Messaging;

/// <summary>
/// Identifies one configured message store: the unit of persistence that holds an outbox and an
/// inbox, such as one <c>DbContext</c> configured with <c>bus.UseEntityFrameworkOutbox&lt;TContext&gt;()</c>.
/// </summary>
/// <param name="Value">The opaque key. Its format belongs to the store provider.</param>
/// <remarks>
/// <para>
/// A store is a unit of persistence, not a tenant. Application code needs a key only to choose a
/// store explicitly through <see cref="PublishOptions.Store"/>, and obtains it from the store
/// provider (for EF Core, <c>EntityFrameworkOutbox.KeyOf&lt;TContext&gt;()</c>) rather than by
/// writing the string.
/// </para>
/// <para>Two keys are equal when their values are equal, ordinally.</para>
/// </remarks>
public sealed record MessageStoreKey(string Value)
{
    /// <summary>Gets the opaque key.</summary>
    public string Value { get; } = !string.IsNullOrWhiteSpace(Value)
        ? Value
        : throw new ArgumentException("A store key cannot be empty or whitespace.", nameof(Value));

    /// <inheritdoc />
    public override string ToString() => Value;
}
