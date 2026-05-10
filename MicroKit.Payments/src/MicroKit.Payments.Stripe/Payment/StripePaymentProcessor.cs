using Ardalis.Result;
using MicroKit.Payments.Abstractions;
using Microsoft.Extensions.Logging;
using Stripe;

namespace MicroKit.Payments.Stripe.Payment;

internal sealed class StripePaymentProcessor : IPaymentProcessor
{
    private readonly IStripeClient _client;
    private readonly PaymentIntentService _paymentIntentService;
    private readonly ILogger<StripePaymentProcessor> _logger;

    public StripePaymentProcessor(
        IStripeClient stripeClient,
        ILogger<StripePaymentProcessor> logger)
    {
        _client = stripeClient;
        _paymentIntentService = new PaymentIntentService(_client);
        _logger = logger;
    }

    public async Task<Result<PaymentResponse>> CreatePaymentAsync(
        CreatePaymentRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var options = new PaymentIntentCreateOptions
            {
                Amount = request.AmountInSubUnits, // Reçu tel quel, prêt pour Stripe //ToStripeAmount(request.Amount),
                Currency = request.Currency.ToLowerInvariant(),
                Customer = request.CustomerId,
                PaymentMethod = request.PaymentMethodId,
                Description = request.Description,
                Metadata = request.Metadata?.ToDictionary(k => k.Key, v => v.Value),
                Confirm = request.PaymentMethodId != null, // On confirme si on a une carte
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true
                }
            };

            var requestOptions = new RequestOptions
            {
                IdempotencyKey = request.IdempotencyKey
            };

            var intent = await _paymentIntentService.CreateAsync(
                options,
                requestOptions,
                ct);
            return intent.Status switch
            {
                "succeeded" => Result.Success(new PaymentResponse(intent.Id, PaymentStatus.Succeeded, intent.ClientSecret)),

                "requires_action" or "requires_confirmation"
                    => Result.Success(new PaymentResponse(intent.Id, PaymentStatus.RequiresAction, intent.ClientSecret)),

                "requires_payment_method"
                    => Result.Invalid(new ValidationError { Identifier = "PaymentMethod", ErrorMessage = "La carte a été déclinée." }),

                _ => Result.Error($"Statut de paiement inattendu : {intent.Status}")
            };
            
        }
        catch (StripeException ex)
        {
            return HandleStripeException<PaymentResponse>(ex);
        }
    }

    private static long ToStripeAmount(decimal amount)
        => (long)Math.Round(amount * 100, MidpointRounding.AwayFromZero);

    private Result<T> HandleStripeException<T>(StripeException ex)
    {
        _logger.LogError(ex,
            "Stripe error: {Code} - {Message}",
            ex.StripeError?.Code,
            ex.StripeError?.Message);

        return ex.StripeError?.Type switch
        {
            "card_error" => Result.Invalid(
            [
                new ValidationError(ex.StripeError.Code, ex.StripeError.Message)
            ]),

            "invalid_request_error" => Result.Invalid(new[]
            {
                new ValidationError(ex.StripeError.Param, ex.StripeError.Message)
            }),

            "authentication_error" => Result.Unauthorized(),

            _ => Result.Error(ex.StripeError?.Message ?? "Une erreur de paiement est survenue.")
        };
    }
}
