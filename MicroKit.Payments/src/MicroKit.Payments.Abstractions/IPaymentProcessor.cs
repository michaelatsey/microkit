using Ardalis.Result;

namespace MicroKit.Payments.Abstractions;

public interface IPaymentProcessor
{
    Task<Result<PaymentResponse>> CreatePaymentAsync(
        CreatePaymentRequest request,
        CancellationToken ct = default);
}

public sealed record CreatePaymentRequest(

    long AmountInSubUnits,
    string Currency,
    string CustomerId,
    string? PaymentMethodId = null,
    string? Description = null,
    string? IdempotencyKey = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public enum PaymentStatus { Succeeded, Failed, Pending, RequiresAction }

public sealed record PaymentResponse(
    string TransactionId,
    PaymentStatus Status,
    string? ClientSecret = null,
    string? ErrorMessage = null
    );