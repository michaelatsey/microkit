using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Payments.Abstractions;

public sealed record PaymentError(
    string Code,
    string Message,
    string? StripeDeclineCode = null);
