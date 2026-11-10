namespace MicroKit.Result;

/// <summary>
/// What a transport needs to know about an <see cref="ErrorCategory"/> before it writes an error for a client.
/// </summary>
public static class ErrorCategoryExtensions
{
    /// <summary>
    /// Gets the text a client reads in place of the message of a fault of the system:
    /// <c>"The server could not complete the request."</c>
    /// </summary>
    /// <remarks>A property, not a <see langword="const"/>: a constant would be copied into each consuming assembly.</remarks>
    public static string SystemFaultMessage { get; } = "The server could not complete the request.";

    /// <summary>
    /// Determines whether the category is a fault of the system: <see cref="ErrorCategory.Technical"/>,
    /// <see cref="ErrorCategory.External"/>, <see cref="ErrorCategory.Unavailable"/>, <see cref="ErrorCategory.Timeout"/>,
    /// <see cref="ErrorCategory.NotSupported"/>, or any undefined value.
    /// </summary>
    /// <remarks>
    /// Such an error's message may describe the system (a host, a query, an exception's text): a writer meant for
    /// clients writes <see cref="SystemFaultMessage"/> instead. It is also the group of highest precedence in an
    /// <see cref="ErrorCollection"/>.
    /// </remarks>
    /// <param name="category">The category.</param>
    /// <returns><see langword="true"/> for a fault of the system; otherwise <see langword="false"/>.</returns>
    public static bool IsSystemFault(this ErrorCategory category) => ErrorCategoryFacts.IsSystemFault(category);

    /// <summary>
    /// Determines whether an error of the category describes the request's input: <see cref="ErrorCategory.Validation"/>
    /// or <see cref="ErrorCategory.BusinessRule"/>.
    /// </summary>
    /// <remarks>
    /// Only such an error names a field to a client; a collection of any other category names, for a client, only its
    /// errors of that category.
    /// </remarks>
    /// <param name="category">The category.</param>
    /// <returns><see langword="true"/> for <see cref="ErrorCategory.Validation"/> and <see cref="ErrorCategory.BusinessRule"/>; otherwise <see langword="false"/>.</returns>
    public static bool DescribesInput(this ErrorCategory category) => ErrorCategoryFacts.DescribesInput(category);
}
