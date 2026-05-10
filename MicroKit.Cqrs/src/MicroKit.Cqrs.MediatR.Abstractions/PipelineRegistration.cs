namespace MicroKit.Cqrs.MediatR.Abstractions;

public record PipelineRegistration(Type Type, int Order);
