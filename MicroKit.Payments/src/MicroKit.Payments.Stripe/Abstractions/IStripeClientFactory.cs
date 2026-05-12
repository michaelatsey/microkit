using Stripe;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Payments.Stripe.Abstractions
{
    /// <summary>Factory that creates <see cref="StripeClient"/> instances for a given API key.</summary>
    public interface IStripeClientFactory
    {
        /// <summary>Creates a <see cref="StripeClient"/> configured with the supplied <paramref name="apiKey"/>.</summary>
        /// <param name="apiKey">The Stripe secret API key.</param>
        /// <returns>A configured <see cref="StripeClient"/> instance.</returns>
        StripeClient Create(string apiKey);
    }
}
