namespace MicroKit.Domain.Identifiers;

/// <summary>
/// Direct Guid-based identifier for simple cases.
/// Prefer specific record structs like OrderId(Guid) for better type safety.
/// </summary>
public readonly record struct GuidId(Guid Value) : IEntityId
{
    object IEntityId.Value => Value;

    public static GuidId New() => new(Guid.NewGuid());
    public static GuidId Empty => new(Guid.Empty);
    public static GuidId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}