using Ardalis.Result;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Payments.Abstractions;

public interface ISubscriptionProcessor
{
    Task<Result<RetriveSubscriptionResponse>> RetrieveSubscriptionAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default);

    Task<Result<SubscriptionResponse>> CreateSubscriptionAsync(
        SubscriptionRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<SubscriptionCancelResponse>> CancelSubscriptionAsync(
        string subscriptionId,
        bool prorate = true,
        CancellationToken cancellationToken = default);
}
public sealed record SubscriptionRequest(
    string CustomerId,
    string ProviderPriceId,
    string? PaymentMethodId = null,
    Dictionary<string, string>? Metadata = null,
    bool CancelAtPeriodEnd = false);

public sealed record SubscriptionResponse(
    string SubscriptionId,
    string Status,
    string? ClientSecret, // Indispensable pour Stripe Elements
    DateTime? StartDate,
    DateTime? EndDate,
    string? LatestInvoiceId = null,
    string? ProviderPlanId = null);

public sealed record RetriveSubscriptionResponse(
    string SubscriptionId,
    string CustomerId,
    string Status,
    string? ProviderPlanId,
    string? ProviderProductId,
    DateTime StartDate,
    DateTime? EndDate,
    bool CancelAtPeriodEnd
    );

public sealed record SubscriptionCancelResponse(string SubscriptionId,
    string Status, // Sera "canceled"
    DateTime? CanceledAt, // Date de la demande
    DateTime? EndedAt,    // Date de fin effective de l'accès
    string? ProviderPlanId);