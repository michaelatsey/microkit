namespace MicroKit.Auth.AspNetCore;

/// <summary>
/// An <see cref="IAuthorizationRequirement"/> that carries a typed <see cref="Permission"/>
/// to be evaluated by <see cref="PermissionAuthorizationHandler"/>.
/// </summary>
/// <param name="Permission">The permission the current user must hold.</param>
public sealed record PermissionAuthorizationRequirement(Permission Permission)
    : IAuthorizationRequirement;
