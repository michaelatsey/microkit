using Ardalis.Result;
using MicroKit.Payments.Abstractions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Stripe;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Payments.Stripe.Subscription;

internal sealed class StripeSubscriptionProcessor : ISubscriptionProcessor
{
    private readonly SubscriptionService _subscriptionService;
    private readonly ILogger<StripeSubscriptionProcessor> _logger;

    public StripeSubscriptionProcessor(IStripeClient stripeClient, ILogger<StripeSubscriptionProcessor> logger)
    {
        _subscriptionService = new SubscriptionService(stripeClient);
        _logger = logger;
    }

    public async Task<Result<RetriveSubscriptionResponse>> RetrieveSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new SubscriptionGetOptions
            {
                Expand = ["items.data.price"] // On expand pour avoir le ProductId
            };
            var stripeSub = await _subscriptionService.GetAsync(subscriptionId, options, null, cancellationToken: cancellationToken);

            if (stripeSub == null)
                return Result.NotFound("Subscription not found in Stripe.");

            // On récupère le premier item (le plan principal)
            var primaryItem = stripeSub.Items.Data.FirstOrDefault();

            // Extraction des IDs
            var planId = primaryItem?.Price?.Id; // price_1Mow... (Ton PlanID)
            var productId = primaryItem?.Price?.ProductId; // prod_Na6d... (Ton ProductID)

            // Récupération des dates (puisqu'elles sont présentes dans l'item du JSON)
            // Dans Stripe.net, primaryItem a souvent des propriétés PeriodStart / PeriodEnd
            // Si elles n'y sont pas, on utilise LatestInvoice comme vu précédemment.
            var endDate = primaryItem?.CurrentPeriodEnd ?? stripeSub.StartDate;

            return Result.Success(new RetriveSubscriptionResponse(
                stripeSub.Id, 
                stripeSub.CustomerId,
                stripeSub.Status,
                primaryItem?.Price?.Id,
                primaryItem?.Price?.ProductId,
                stripeSub.StartDate,
                primaryItem?.CurrentPeriodEnd, // Date de fin précise de l'item
                stripeSub.CancelAtPeriodEnd
            ));
        }
        catch (StripeException ex)
        {
            return Result.Error(ex.Message);
        }
    }

    public async Task<Result<SubscriptionResponse>> CreateSubscriptionAsync(
        SubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.CustomerId))
                return Result.Invalid(new ValidationError(nameof(request.CustomerId), "CustomerId is required"));

            if (string.IsNullOrWhiteSpace(request.ProviderPriceId))
                return Result.Invalid(new ValidationError(nameof(request.ProviderPriceId), "ProviderPriceId is required"));

            var options = new SubscriptionCreateOptions
            {
                Customer = request.CustomerId,
                Items =
                [
                    new() 
                    {
                        Price = request.ProviderPriceId
                    }
                ],
                DefaultPaymentMethod = request.PaymentMethodId,
                Metadata = request.Metadata,
                // "default_incomplete" est essentiel pour le 3D Secure
                PaymentBehavior = "default_incomplete",

                // On demande à Stripe d'inclure les détails du paiement dans la réponse
                AddInvoiceItems = [],
                // IMPORTANT: On expand "latest_invoice" mais plus besoin d'expand "payment_intent" 
                // car confirmation_secret est inclus par défaut ou via l'objet invoice.
                Expand = ["latest_invoice"]
            };
            var stripeSubscription = await _subscriptionService.CreateAsync(options, cancellationToken: cancellationToken);

            string? clientSecret = null;

            // NOUVELLE LOGIQUE POST-BASIL :
            // On utilise confirmation_secret au lieu de payment_intent
            if (stripeSubscription.LatestInvoice != null)
            {
                // confirmation_secret contient le client_secret nécessaire pour le Payment Element
                clientSecret = stripeSubscription.LatestInvoice.ConfirmationSecret?.ClientSecret;
            }

            // Récupération des dates via l'Invoice (Post-Basil)
            // Note: Si LatestInvoice n'est pas encore généré (rare), on fallback sur StartDate
            var periodEnd = stripeSubscription.LatestInvoice?.PeriodEnd;
            var periodStart = stripeSubscription.LatestInvoice?.PeriodStart ?? stripeSubscription.StartDate;

            return Result.Success(new SubscriptionResponse(
                stripeSubscription.Id,
                stripeSubscription.Status,
                clientSecret,
                periodStart,
                periodEnd,
                stripeSubscription.LatestInvoiceId,
                stripeSubscription.Items.Data.FirstOrDefault()?.Price?.Id
                ));
        }
        catch (StripeException ex)
        {
            return HandleStripeException<SubscriptionResponse>(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during subscription creation");
            return Result.Error("An unexpected error occurred.");
        }
    }

    public async Task<Result<SubscriptionCancelResponse>> CancelSubscriptionAsync(
        string subscriptionId,
        bool prorate = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(subscriptionId))
                return Result.Invalid(new ValidationError(nameof(subscriptionId), "SubscriptionId is required"));

            var options = new SubscriptionCancelOptions
            {
                Prorate = prorate,
                InvoiceNow = true
            };

            var subscription = await _subscriptionService.CancelAsync(
                subscriptionId,
                options,
                cancellationToken: cancellationToken);

            return Result.Success(new SubscriptionCancelResponse(
                subscription.Id,
                subscription.Status,
                subscription.CanceledAt, // 1678768842 dans ton JSON
                subscription.EndedAt,    // 1678768842 dans ton JSON
                subscription.Items?.Data?.FirstOrDefault()?.Price?.Id));
        }
        catch (StripeException ex)
        {
            return HandleStripeException<SubscriptionCancelResponse>(ex);
        }
    }

    private Result<T> HandleStripeException<T>(StripeException ex)
    {
        _logger.LogError(ex, "Stripe Subscription Error: {Message}", ex.StripeError?.Message);

        // On réutilise la logique de mapping d'erreurs pour rester cohérent
        return ex.StripeError?.Type switch
        {
            "card_error" => Result.Invalid(new ValidationError { Identifier = ex.StripeError.Code, ErrorMessage = ex.StripeError.Message }),
            _ => Result.Error(ex.StripeError?.Message ?? "Une erreur est survenue lors de la gestion de l'abonnement.")
        };
    }

    
}
