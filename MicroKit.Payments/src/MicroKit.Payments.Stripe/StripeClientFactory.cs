using MicroKit.Payments.Stripe.Abstractions;
using Stripe;

namespace MicroKit.Payments.Stripe;

internal sealed class StripeClientFactory : IStripeClientFactory
{
    public StripeClient Create(string apiKey)
        => new(apiKey);
}
