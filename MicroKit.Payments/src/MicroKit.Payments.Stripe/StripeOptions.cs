using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Payments.Abstractions.Builder;

public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    public string SecretKey { get; init; } = default!;
    public string WebhookSecret { get; init; } = default!;
}
