namespace MicroKit.Tenancy;

/// <summary>Well-known errors produced by MicroKit.Tenancy.</summary>
public static class MultitenancyErrors
{
    /// <summary>Tenant not found in the store.</summary>
    public static readonly Error TenantNotFound = new TenantNotFoundError();

    /// <summary>Tenant identifier has an invalid format.</summary>
    public static readonly Error InvalidTenantId = new InvalidTenantIdError();

    /// <summary>Tenant exists but is inactive.</summary>
    public static readonly Error TenantInactive = new TenantInactiveError();

    /// <summary>No resolution strategy could identify the current tenant.</summary>
    public static readonly Error ResolutionFailed = new ResolutionFailedError();
}
