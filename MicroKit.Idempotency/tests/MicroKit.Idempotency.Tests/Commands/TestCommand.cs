using MicroKit.Idempotency.Abstractions.Contracts;

namespace MicroKit.Idempotency.Tests.Commands;

public record TestCommand : IIdempotentRequest<TestResponse>
{
    public string IdempotencyKey { get; init; } = "";
    public TimeSpan? IdempotencyExpiration => null;
    public string Data { get; init; } = "";
}

public record TestResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
}
