using Ardalis.Result;

namespace MicroKit.Payments.Abstractions;

public interface IRefundProcessor
{
    Task<Result<RefundResponse>> RefundAsync(
        RefundRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record RefundRequest(
    string PaymentIntentId,
    long? AmountInSubUnits = null, // Ex: 1000 pour 10.00€
    string? Currency = null,       // Requis par certains providers pour validation
    string? Reason = null,
    Dictionary<string, string>? Metadata = null);

public sealed record RefundResponse(string Id, string PaymentIntentId, PaymentStatus Status, DateTime CreatedAt);