namespace MicroKit.Auth.AspNetCore;

/// <summary>
/// Specifies that the decorated controller, action, or endpoint requires the current user to hold
/// the given permission. Works with ASP.NET Core's standard <c>UseAuthorization()</c> pipeline
/// via <see cref="PermissionPolicyProvider"/> and <see cref="PermissionAuthorizationHandler"/>.
/// </summary>
/// <remarks>
/// <para>
/// Generates a policy name of the form
/// <c>MicroKit.Auth.Permission:{resource}:{action}</c>,
/// which <see cref="PermissionPolicyProvider"/> resolves to an <see cref="AuthorizationPolicy"/>
/// containing a <see cref="PermissionAuthorizationRequirement"/>.
/// </para>
/// <para>
/// The policy includes <c>RequireAuthenticatedUser()</c>, so unauthenticated requests
/// receive a 401 challenge before the permission check runs.
/// </para>
/// <para>
/// Multiple instances may be stacked on the same target — all required permissions must be held.
/// </para>
/// </remarks>
/// <param name="resource">
/// The resource portion of the permission (e.g. <c>"audits"</c>, <c>"non-conformities"</c>).
/// Lowercase, kebab-case.
/// </param>
/// <param name="action">
/// The action portion of the permission (e.g. <c>"read"</c>, <c>"create"</c>).
/// Lowercase.
/// </param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute(string resource, string action)
    : AuthorizeAttribute($"{PermissionPolicyProvider.PolicyPrefix}{resource}:{action}")
{
    /// <summary>
    /// The typed permission derived from the <c>resource</c> and <c>action</c> constructor parameters.
    /// </summary>
    public Permission Permission { get; } = Permission.Of(resource, action);
}
