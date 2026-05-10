using Ardalis.Result;
using MicroKit.Payments.Abstractions;
using Microsoft.Extensions.Logging;
using Stripe;

namespace MicroKit.Payments.Stripe.Refund;

internal class StripeRefundProcessor : IRefundProcessor
{
    private readonly RefundService _refundService;
    private readonly ILogger<StripeRefundProcessor> _logger;

    public StripeRefundProcessor(IStripeClient stripeClient, ILogger<StripeRefundProcessor> logger)
    {
        _refundService = new RefundService(stripeClient);
        _logger = logger;
    }

    public async Task<Result<RefundResponse>> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new RefundCreateOptions
            {
                PaymentIntent = request.PaymentIntentId,
                // On utilise directement la valeur brute (Stripe attend déjà des centimes)
                Amount = request.AmountInSubUnits,
                Reason = request.Reason,
                Metadata = request.Metadata
            };

            var refund = await _refundService.CreateAsync(options, cancellationToken: cancellationToken);

            return refund.Status switch
            {
                "succeeded" => Result.Success(new RefundResponse(
                    refund.Id,
                    refund.PaymentIntentId,
                    PaymentStatus.Succeeded,
                    refund.Created)),
                _ => Result.Error($"Refund status: {refund.Status}")
            };

        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error during refund.");
            return Result.Error($"Stripe error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during refund.");
            return Result.Error("An unexpected error occurred.");
        }
    }
}
