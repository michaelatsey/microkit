namespace MicroKit.Domain.Services;

/// <summary>
/// Marker interface for domain services.
/// Domain services contain domain logic that doesn't naturally fit within any entity or value object.
/// Should be stateless and contain pure domain logic only.
/// </summary>
public interface IDomainService
{
    // Marker interface - no members
    // Implementations should be stateless and focused on domain logic
    // Examples: PolicyEvaluationService, PricingService, etc.
}