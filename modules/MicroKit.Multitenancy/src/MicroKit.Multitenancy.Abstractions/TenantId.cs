namespace MicroKit.Multitenancy;

/// <summary>Strongly-typed tenant identifier. Wraps a <see cref="Guid"/>.</summary>
public sealed record TenantId(Guid Value)
{
    /// <summary>Creates a new random <see cref="TenantId"/>.</summary>
    public static TenantId NewId() => new(Guid.NewGuid());

    /// <summary>Returns the string representation of the underlying <see cref="Guid"/>.</summary>
    public override string ToString() => Value.ToString();
}
