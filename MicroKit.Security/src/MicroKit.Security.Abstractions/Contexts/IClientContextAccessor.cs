namespace MicroKit.Security.Abstractions.Contexts;
/// <summary>
/// Provides access to the current client context.
/// Similar to IHttpContextAccessor pattern.
/// </summary>
public interface IClientContextAccessor
{
    /// <summary>
    /// Gets or sets the current client context.
    /// </summary>
    IClientContext? Context { get; set; }
}
