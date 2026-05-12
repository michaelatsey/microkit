namespace MicroKit.Cqrs.MediatR.Abstractions;

/// <summary>Represents a MediatR pipeline behavior registration with an explicit ordering.</summary>
/// <param name="Type">The pipeline behavior type to register.</param>
/// <param name="Order">The position of this behavior in the pipeline (lower values run first).</param>
public record PipelineRegistration(Type Type, int Order);
