using Ardalis.Result;

namespace MicroKit.Payments.Abstractions;

/// <summary>Creates and retrieves payment gateway customers.</summary>
public interface ICustomerGateway
{
    /// <summary>Creates a new customer in the payment gateway.</summary>
    /// <param name="request">The customer creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the created customer or error details.</returns>
    Task<Result<CustomerResponse>> CreateAsync(
        CreateCustomerRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves an existing customer by their gateway identifier.</summary>
    /// <param name="customerId">The gateway-assigned customer identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the customer data or error details.</returns>
    Task<Result<CustomerResponse>> GetAsync(
        string customerId,
        CancellationToken cancellationToken = default);
}

/// <summary>Request payload for creating a new customer in the payment gateway.</summary>
/// <param name="Email">The customer's email address.</param>
/// <param name="Name">The customer's display name.</param>
/// <param name="Phone">Optional phone number.</param>
/// <param name="Metadata">Optional key-value metadata to attach to the customer record.</param>
/// <param name="IdempotencyKey">Optional idempotency key to prevent duplicate creation.</param>
public record CreateCustomerRequest(
    string Email,
    string Name,
    string? Phone = null,
    Dictionary<string, string>? Metadata = null,
    string? IdempotencyKey = null
);

/// <summary>Represents a customer record returned by the payment gateway.</summary>
/// <param name="Id">The gateway-assigned customer identifier.</param>
/// <param name="Email">The customer's email address.</param>
/// <param name="Name">The customer's display name.</param>
/// <param name="Phone">The customer's phone number.</param>
/// <param name="CreatedAt">The UTC timestamp when the customer was created.</param>
/// <param name="Metadata">Optional key-value metadata attached to the customer.</param>
public record CustomerResponse(
    string Id,
    string Email,
    string Name,
    string? Phone,
    DateTime CreatedAt,
    Dictionary<string, string>? Metadata = null
);
