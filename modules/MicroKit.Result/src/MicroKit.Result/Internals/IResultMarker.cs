namespace MicroKit.Result;

/// <summary>
/// Marks <see cref="Result"/> and <see cref="Result{T}"/>, so the implicit conversion from a value can
/// reject a result passed as that value without reflection.
/// </summary>
internal interface IResultMarker;
