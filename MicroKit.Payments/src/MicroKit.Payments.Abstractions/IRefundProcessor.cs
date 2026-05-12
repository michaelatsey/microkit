using Ardalis.Result;

namespace MicroKit.Payments.Abstractions;

/// <summary>Issues refunds against previously charged payments.</summary>
public interface IRefundProcessor
{
    /// <summary>Issues a (full or partial) refund for a payment.</summary>
    /// <param name="request">The refund request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the refund response or error details.</returns>
    Task<Result<RefundResponse>> RefundAsync(
        RefundRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Request payload for issuing a refund.</summary>
/// <param name="PaymentIntentId">The identifier of the payment to refund.</param>
/// <param name="AmountInSubUnits">Optional partial refund amount in the currency's smallest unit; omit for full refund.</param>
/// <param name="Currency">Optional currency code for validation by certain providers.</param>
/// <param name="Reason">Optional reason for the refund.</param>
/// <param name="Metadata">Optional key-value metadata to attach to the refund.</param>
public sealed record RefundRequest(
    string PaymentIntentId,
    long? AmountInSubUnits = null,
    string? Currency = null,
    string? Reason = null,
    Dictionary<string, string>? Metadata = null);

/// <summary>Represents the response returned after a refund is issued.</summary>
/// <param name="Id">The gateway-assigned refund identifier.</param>
/// <param name="PaymentIntentId">The original payment identifier that was refunded.</param>
/// <param name="Status">The current status of the refund.</param>
/// <param name="CreatedAt">The UTC timestamp when the refund was created.</param>
public sealed record RefundResponse(string Id, string PaymentIntentId, PaymentStatus Status, DateTime CreatedAt);
