using Stripe;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Payments.Stripe.Abstractions
{
    public interface IStripeClientFactory
    {
        StripeClient Create(string apiKey);
    }
}
