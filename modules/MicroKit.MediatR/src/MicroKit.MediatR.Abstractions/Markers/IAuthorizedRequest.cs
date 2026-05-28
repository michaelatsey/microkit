namespace MicroKit.MediatR;

/// <summary>
/// Opts a request into <c>AuthorizationBehavior</c> (pipeline order 200).
/// All policies in <see cref="RequiredPolicies"/> must pass before the handler is invoked.
/// On failure the behavior short-circuits with an unauthorized result or exception.
/// </summary>
/// <example>
/// <code>
/// public sealed record DeleteUserCommand(Guid UserId)
///     : ICommand&lt;Result&lt;Unit&gt;&gt;, IAuthorizedRequest
/// {
///     public string[] RequiredPolicies => ["Admin"];
/// }
/// </code>
/// </example>
public interface IAuthorizedRequest
{
    /// <summary>
    /// Non-empty list of ASP.NET Core authorization policy names that must all succeed.
    /// Evaluated via <c>IAuthorizationService</c>.
    /// </summary>
    string[] RequiredPolicies { get; }
}
