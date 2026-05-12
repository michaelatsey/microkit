using MicroKit.Idempotency.Abstractions.Contracts;

namespace MicroKit.Idempotency.Tests.Commands;

/// <summary>Test command used in idempotency behavior unit tests.</summary>
public record TestCommand : IIdempotentRequest<TestResponse>
{
    /// <inheritdoc/>
    public string IdempotencyKey { get; init; } = "";
    /// <inheritdoc/>
    public TimeSpan? IdempotencyExpiration => null;
    /// <summary>Gets or sets arbitrary payload data for the test command.</summary>
    public string Data { get; init; } = "";
}

/// <summary>Test response returned by the idempotency behavior test handler.</summary>
public record TestResponse
{
    /// <summary>Gets or sets whether the operation succeeded.</summary>
    public bool Success { get; init; }
    /// <summary>Gets or sets a descriptive message.</summary>
    public string Message { get; init; } = "";
}
