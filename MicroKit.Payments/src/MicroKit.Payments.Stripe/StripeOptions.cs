using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Payments.Abstractions.Builder;

/// <summary>Configuration options for the Stripe payment integration.</summary>
public sealed class StripeOptions
{
    /// <summary>The configuration section name for Stripe options.</summary>
    public const string SectionName = "Stripe";

    /// <summary>Gets or sets the Stripe secret API key.</summary>
    public string SecretKey { get; init; } = default!;
    /// <summary>Gets or sets the Stripe webhook signing secret used to verify incoming webhook events.</summary>
    public string WebhookSecret { get; init; } = default!;
}
