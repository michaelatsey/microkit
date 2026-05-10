namespace MicroKit.Abstractions.Contexts;

public interface ITenantIdAccessor
{
    string? TenantId { get; }
}
