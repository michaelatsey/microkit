using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Payments.Abstractions;

/// <summary>Represents a payment gateway error with a machine-readable code and human-readable message.</summary>
/// <param name="Code">The gateway-specific error code.</param>
/// <param name="Message">A human-readable description of the error.</param>
/// <param name="StripeDeclineCode">Optional Stripe-specific decline code for card failures.</param>
public sealed record PaymentError(
    string Code,
    string Message,
    string? StripeDeclineCode = null);
