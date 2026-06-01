using System.Security.Claims;

namespace MicroKit.MediatR;

/// <summary>
/// Provides the current authenticated principal for use by
/// <c>AuthorizationBehavior</c> (pipeline order 200).
/// </summary>
/// <remarks>
/// Register an implementation appropriate for the host environment:
/// <list type="bullet">
/// <item><description>ASP.NET Core apps — <c>HttpContextCurrentUserAccessor</c> (provided in <c>MicroKit.MediatR.Behaviors</c>).</description></item>
/// <item><description>Worker services and message consumers — a custom implementation backed by the relevant context.</description></item>
/// </list>
/// This interface uses only BCL types (<see cref="ClaimsPrincipal"/> from <c>System.Security.Claims</c>)
/// and adds no extra package dependency to <c>MicroKit.MediatR.Abstractions</c>.
/// </remarks>
public interface ICurrentUserAccessor
{
    /// <summary>
    /// The current authenticated principal, or <see langword="null"/> if unauthenticated
    /// or running outside an authenticated context.
    /// </summary>
    ClaimsPrincipal? Current { get; }
}
