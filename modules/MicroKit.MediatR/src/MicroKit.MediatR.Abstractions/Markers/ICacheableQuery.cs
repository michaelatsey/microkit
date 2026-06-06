namespace MicroKit.MediatR.Markers;

/// <summary>
/// Opts a query into <c>CachingBehavior</c> (pipeline order 500).
/// The query result is cached under <see cref="CacheKey"/> for <see cref="Expiry"/>.
/// </summary>
/// <example>
/// <code>
/// public sealed record GetUserByIdQuery(Guid UserId) : IQuery&lt;Result&lt;UserDto&gt;&gt;, ICacheableQuery
/// {
///     public string CacheKey => $"user:{UserId}";
///     public TimeSpan? Expiry => TimeSpan.FromMinutes(5);
/// }
/// </code>
/// </example>
public interface ICacheableQuery
{
    /// <summary>The distributed cache key for this query's result. Must be non-null and non-empty.</summary>
    string CacheKey { get; }

    /// <summary>
    /// Cache entry lifetime. <see langword="null"/> means no expiry — the behavior will log a WARNING
    /// at runtime when this is <see langword="null"/>.
    /// </summary>
    TimeSpan? Expiry { get; }
}
