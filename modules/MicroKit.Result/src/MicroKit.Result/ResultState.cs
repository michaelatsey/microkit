namespace MicroKit.Result;

/// <summary>
/// Represents the state of a result. Used for conceptual clarity only;
/// the actual struct discriminator uses a <see langword="byte"/> tag field.
/// </summary>
internal enum ResultState : byte
{
    /// <summary>The operation succeeded.</summary>
    Success = 0,

    /// <summary>The operation failed.</summary>
    Failure = 1,
}
