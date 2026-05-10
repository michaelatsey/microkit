using Ardalis.Result;

namespace MicroKit.Payments.Abstractions;

public interface ICustomerGateway
{
    Task<Result<CustomerResponse>> CreateAsync(
        CreateCustomerRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<CustomerResponse>> GetAsync(    
        string customerId,
        CancellationToken cancellationToken = default);
}

public record CreateCustomerRequest(
        string Email,
        string Name,
        string? Phone = null,
        Dictionary<string, string>? Metadata = null,
        string? IdempotencyKey = null
    );

public record CustomerResponse(
    string Id,
    string Email,
    string Name,
    string? Phone,
    DateTime CreatedAt,
    Dictionary<string, string>? Metadata = null
);