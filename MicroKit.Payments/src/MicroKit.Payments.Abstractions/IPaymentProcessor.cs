using Ardalis.Result;

namespace MicroKit.Payments.Abstractions;

/// <summary>Processes payment charges against a payment gateway.</summary>
public interface IPaymentProcessor
{
    /// <summary>Creates a new payment charge.</summary>
    /// <param name="request">The payment creation request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result containing the payment response or error details.</returns>
    Task<Result<PaymentResponse>> CreatePaymentAsync(
        CreatePaymentRequest request,
        CancellationToken ct = default);
}

/// <summary>Request payload for creating a payment charge.</summary>
/// <param name="AmountInSubUnits">The amount in the currency's smallest unit (e.g. cents).</param>
/// <param name="Currency">ISO 4217 currency code (e.g. <c>EUR</c>).</param>
/// <param name="CustomerId">The gateway-assigned customer identifier.</param>
/// <param name="PaymentMethodId">Optional saved payment method identifier.</param>
/// <param name="Description">Optional human-readable description.</param>
/// <param name="IdempotencyKey">Optional idempotency key to prevent duplicate charges.</param>
/// <param name="Metadata">Optional key-value metadata for the payment.</param>
public sealed record CreatePaymentRequest(
    long AmountInSubUnits,
    string Currency,
    string CustomerId,
    string? PaymentMethodId = null,
    string? Description = null,
    string? IdempotencyKey = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>Represents the lifecycle status of a payment charge.</summary>
public enum PaymentStatus
{
    /// <summary>The payment was successfully processed.</summary>
    Succeeded,
    /// <summary>The payment attempt failed.</summary>
    Failed,
    /// <summary>The payment is awaiting processing.</summary>
    Pending,
    /// <summary>The payment requires additional action from the customer (e.g. 3D Secure).</summary>
    RequiresAction
}

/// <summary>Represents the response returned after a payment charge attempt.</summary>
/// <param name="TransactionId">The gateway-assigned transaction identifier.</param>
/// <param name="Status">The current payment status.</param>
/// <param name="ClientSecret">Optional client secret for front-end confirmation flows (e.g. Stripe Elements).</param>
/// <param name="ErrorMessage">Optional error message if the charge failed.</param>
public sealed record PaymentResponse(
    string TransactionId,
    PaymentStatus Status,
    string? ClientSecret = null,
    string? ErrorMessage = null
);
