namespace MicroKit.Result.Serialization;

/// <summary>
/// What the result converters write: the view a client may read, or the whole result for in-process storage
/// (ADR-RESULT-001 D1, D12).
/// </summary>
public enum ResultJsonView
{
    /// <summary>
    /// The default: what <c>ToHttpResult</c> shows a client. No <see cref="IError.Metadata"/>; the message of a fault
    /// of the system (<see cref="ErrorCategoryExtensions.IsSystemFault"/>) is <see cref="ErrorCategoryExtensions.SystemFaultMessage"/>,
    /// in a collection too; a collection whose category does not describe the input
    /// (<see cref="ErrorCategoryExtensions.DescribesInput"/>) names only its errors of that category, nested
    /// collections flattened, and no error names a field outside an input category. A round trip reads that view back.
    /// </summary>
    Client = 0,

    /// <summary>
    /// The whole result, for in-process storage such as a cache or an idempotency store, never a response sent to a
    /// client: <see cref="IError.Metadata"/>, every message and every error of a collection, <c>propertyName</c>
    /// included, so a round trip returns the result as it was. Metadata values come back as JSON primitives: an integer
    /// as a <see cref="long"/> (or a <see cref="ulong"/>), a non-integer as a <see cref="double"/>, a
    /// <see cref="Guid"/> or a <see cref="DateTime"/> as a <see cref="string"/>, any other object as its invariant string.
    /// </summary>
    Full = 1,
}
