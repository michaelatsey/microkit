namespace MicroKit.Cqrs.Abstractions.Queries;

/// <summary>
/// Marker interface for queries.
/// </summary>
public interface IQuery<out TResponse> { }