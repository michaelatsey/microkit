namespace MicroKit.Result;

/// <summary>
/// Facts about <see cref="ErrorCategory"/> values that the core needs without knowing any transport.
/// </summary>
internal static class ErrorCategoryFacts
{
    private const int InputGroup = 1;
    private const int SystemFaultGroup = 5;

    /// <summary>
    /// The precedence of a category when an <see cref="ErrorCollection"/> holds several: a higher value wins, so the
    /// collection's category never depends on the order of its errors. Groups, highest first: faults of the system
    /// (<see cref="ErrorCategory.Technical"/>, <see cref="ErrorCategory.External"/>, <see cref="ErrorCategory.Unavailable"/>,
    /// <see cref="ErrorCategory.Timeout"/>, <see cref="ErrorCategory.NotSupported"/>, undefined values); access
    /// (<see cref="ErrorCategory.Unauthorized"/>, <see cref="ErrorCategory.Forbidden"/>); the resource
    /// (<see cref="ErrorCategory.NotFound"/>, <see cref="ErrorCategory.Conflict"/>,
    /// <see cref="ErrorCategory.PreconditionFailed"/>, <see cref="ErrorCategory.TooManyRequests"/>);
    /// <see cref="ErrorCategory.Cancelled"/>; the input (<see cref="ErrorCategory.Validation"/>,
    /// <see cref="ErrorCategory.BusinessRule"/>). Inside a group, the category declared first in
    /// <see cref="ErrorCategory"/> wins; undefined values come after every declared one, the smallest first.
    /// </summary>
    internal static long Precedence(ErrorCategory category)
    {
        var group = Group(category);

        // Declaration order breaks ties inside a group; an undefined value ranks after every declared one, by value,
        // so two errors never tie unless they share a category.
        long order = Enum.IsDefined(category) ? (int)category : (1L << 32) + (uint)(int)category;
        return ((long)group << 40) - order;
    }

    /// <summary>
    /// Whether the category is a fault of the system (<see cref="ErrorCategory.Technical"/>, <see cref="ErrorCategory.External"/>,
    /// <see cref="ErrorCategory.Unavailable"/>, <see cref="ErrorCategory.Timeout"/>, <see cref="ErrorCategory.NotSupported"/>,
    /// undefined values): its message may describe the system, so a writer meant for clients never emits it.
    /// </summary>
    internal static bool IsSystemFault(ErrorCategory category) => Group(category) == SystemFaultGroup;

    /// <summary>Whether the category is in the input group (Validation, BusinessRule), the lowest precedence.</summary>
    internal static bool DescribesInput(ErrorCategory category) => Group(category) == InputGroup;

    private static int Group(ErrorCategory category) => category switch
    {
        ErrorCategory.Validation or ErrorCategory.BusinessRule => InputGroup,
        ErrorCategory.Cancelled => 2,
        ErrorCategory.NotFound or ErrorCategory.Conflict or ErrorCategory.PreconditionFailed
            or ErrorCategory.TooManyRequests => 3,
        ErrorCategory.Unauthorized or ErrorCategory.Forbidden => 4,
        _ => SystemFaultGroup, // Technical, External, Unavailable, Timeout, NotSupported, and any undefined value
    };
}
