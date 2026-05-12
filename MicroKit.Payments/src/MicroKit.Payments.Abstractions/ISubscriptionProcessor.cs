using Ardalis.Result;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Payments.Abstractions;

/// <summary>Manages recurring subscription lifecycles against a payment gateway.</summary>
public interface ISubscriptionProcessor
{
    /// <summary>Retrieves a subscription by its gateway identifier.</summary>
    /// <param name="subscriptionId">The gateway-assigned subscription identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the subscription data or error details.</returns>
    Task<Result<RetriveSubscriptionResponse>> RetrieveSubscriptionAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a new subscription for a customer.</summary>
    /// <param name="request">The subscription creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the created subscription or error details.</returns>
    Task<Result<SubscriptionResponse>> CreateSubscriptionAsync(
        SubscriptionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels an active subscription, optionally applying proration.</summary>
    /// <param name="subscriptionId">The gateway-assigned subscription identifier.</param>
    /// <param name="prorate">Whether to prorate the cancellation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the cancellation response or error details.</returns>
    Task<Result<SubscriptionCancelResponse>> CancelSubscriptionAsync(
        string subscriptionId,
        bool prorate = true,
        CancellationToken cancellationToken = default);
}

/// <summary>Request payload for creating a subscription.</summary>
/// <param name="CustomerId">The gateway-assigned customer identifier.</param>
/// <param name="ProviderPriceId">The provider's price/plan identifier.</param>
/// <param name="PaymentMethodId">Optional saved payment method to use for billing.</param>
/// <param name="Metadata">Optional key-value metadata to attach to the subscription.</param>
/// <param name="CancelAtPeriodEnd">Whether to cancel the subscription at the end of the current period.</param>
public sealed record SubscriptionRequest(
    string CustomerId,
    string ProviderPriceId,
    string? PaymentMethodId = null,
    Dictionary<string, string>? Metadata = null,
    bool CancelAtPeriodEnd = false);

/// <summary>Represents the response returned after creating a subscription.</summary>
/// <param name="SubscriptionId">The gateway-assigned subscription identifier.</param>
/// <param name="Status">The current subscription status.</param>
/// <param name="ClientSecret">Client secret for completing front-end confirmation (e.g. Stripe Elements).</param>
/// <param name="StartDate">The subscription start date.</param>
/// <param name="EndDate">The subscription end date, if applicable.</param>
/// <param name="LatestInvoiceId">The identifier of the latest invoice, if available.</param>
/// <param name="ProviderPlanId">The provider-assigned plan identifier.</param>
public sealed record SubscriptionResponse(
    string SubscriptionId,
    string Status,
    string? ClientSecret,
    DateTime? StartDate,
    DateTime? EndDate,
    string? LatestInvoiceId = null,
    string? ProviderPlanId = null);

/// <summary>Represents a full subscription record returned by a retrieve operation.</summary>
/// <param name="SubscriptionId">The gateway-assigned subscription identifier.</param>
/// <param name="CustomerId">The gateway-assigned customer identifier.</param>
/// <param name="Status">The current subscription status.</param>
/// <param name="ProviderPlanId">The provider-assigned plan identifier.</param>
/// <param name="ProviderProductId">The provider-assigned product identifier.</param>
/// <param name="StartDate">The subscription start date.</param>
/// <param name="EndDate">The subscription end date, if applicable.</param>
/// <param name="CancelAtPeriodEnd">Whether the subscription is scheduled to cancel at period end.</param>
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

/// <summary>Represents the response returned after cancelling a subscription.</summary>
/// <param name="SubscriptionId">The cancelled subscription's identifier.</param>
/// <param name="Status">The terminal status (typically <c>canceled</c>).</param>
/// <param name="CanceledAt">The timestamp when the cancellation was requested.</param>
/// <param name="EndedAt">The timestamp when access actually ended.</param>
/// <param name="ProviderPlanId">The provider-assigned plan identifier.</param>
public sealed record SubscriptionCancelResponse(
    string SubscriptionId,
    string Status,
    DateTime? CanceledAt,
    DateTime? EndedAt,
    string? ProviderPlanId);
