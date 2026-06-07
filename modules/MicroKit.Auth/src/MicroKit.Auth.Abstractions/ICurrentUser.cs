namespace MicroKit.Auth;

/// <summary>
/// Represents the currently authenticated user within the request scope.
/// Resolved from the incoming JWT token after successful validation and claims mapping.
/// </summary>
/// <remarks>
/// This interface is produced by an <see cref="IClaimsMapper"/> implementation and
/// stored in the ambient scope via <see cref="ICurrentUserAccessor"/>. Consumers should
/// inject <see cref="ICurrentUserAccessor"/> to read the current user rather than holding
/// a direct reference to this interface across asynchronous operations.
/// </remarks>
public interface ICurrentUser
{
    /// <summary>
    /// Unique identifier of the user, mapped from the JWT <c>sub</c> claim.
    /// </summary>
    Guid UserId { get; }

    /// <summary>
    /// Identifier of the tenant the user is currently operating under.
    /// <see langword="null"/> for non-tenant-scoped operations or single-tenant deployments.
    /// </summary>
    Guid? TenantId { get; }

    /// <summary>
    /// The user's email address, mapped from the JWT <c>email</c> claim.
    /// <see langword="null"/> when the identity provider does not supply an email claim.
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// The roles assigned to this user in the current security context.
    /// Empty when no roles are present; never <see langword="null"/>.
    /// </summary>
    IReadOnlyList<Role> Roles { get; }

    /// <summary>
    /// Gets a value indicating whether the user is authenticated.
    /// <see langword="false"/> for anonymous/guest representations;
    /// <see langword="true"/> for all users resolved from a valid JWT.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Additional claims carried by the JWT, keyed by claim type.
    /// Implementations must guarantee all keys are non-null.
    /// Empty when no extra claims are present; never <see langword="null"/>.
    /// </summary>
    IReadOnlyDictionary<string, string> Claims { get; }
}
