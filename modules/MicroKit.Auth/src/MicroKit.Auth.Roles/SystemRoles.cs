namespace MicroKit.Auth.Roles;

/// <summary>
/// Built-in roles shipped with MicroKit.Auth. Declare application-specific roles
/// in a similar static class alongside your permission registries.
/// </summary>
/// <remarks>
/// All members are eagerly initialised at class load time. Reference these fields
/// in permission maps and test helpers rather than constructing roles from raw strings.
/// </remarks>
public static class SystemRoles
{
    /// <summary>Full system access; bypasses all permission checks.</summary>
    public static readonly Role SuperAdmin = Role.Of("superadmin");

    /// <summary>Administrative access; typically granted all management permissions.</summary>
    public static readonly Role Admin = Role.Of("admin");

    /// <summary>Operational management; elevated but below <see cref="Admin"/>.</summary>
    public static readonly Role Manager = Role.Of("manager");

    /// <summary>Day-to-day operations; standard write access.</summary>
    public static readonly Role Operator = Role.Of("operator");

    /// <summary>Read-access plus audit actions; cannot modify operational data.</summary>
    public static readonly Role Auditor = Role.Of("auditor");

    /// <summary>Read-only access across all resources.</summary>
    public static readonly Role Viewer = Role.Of("viewer");
}
