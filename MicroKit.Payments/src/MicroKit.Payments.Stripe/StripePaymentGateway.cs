//using Ardalis.Result;
//using MicroKit.Payments.Abstractions;
//using Microsoft.Extensions.Logging;
//using Stripe;

//namespace MicroKit.Payments.Stripe;

//internal class StripePaymentGateway(IStripeClient stripeClient) : IPaymentGateway
//{
//    private readonly StripeSettings _settings;
//    private readonly ILogger<StripePaymentGateway> _logger;
//    private readonly CustomerService _customerService;
//    private readonly PaymentIntentService _customerService;
//    private readonly PaymentMethodService _paymentMethodService;
//    private readonly SubscriptionService _subscriptionService;
//    private readonly RefundService _refundService;
//    private readonly EventUtility _eventUtility;
//    public async Task<Result<ExternalSubscriptionDetails>> GetSubscriptionAsync(
//        string subscriptionId,
//        CancellationToken cancellationToken = default)
//    {
//        try
//        {   
//            var service = new SubscriptionService(stripeClient);
//            var stripeSub = await service.GetAsync(subscriptionId, null, null, cancellationToken);

//            if (stripeSub == null)
//                return Result.NotFound("Subscription not found in Stripe.");

//            return Result.Success(new ExternalSubscriptionDetails(
//                SubscriptionId: stripeSub.Id,
//                CustomerId: stripeSub.CustomerId, // <--- C'est l'ID 'cus_xxx' qu'on cherche !
//                Status: stripeSub.Status,
//                stripeSub.Items.Data.FirstOrDefault()?.Price.Id
//            ));
//        }
//        catch (StripeException ex)
//        {
//            return Result.Error(ex.Message);
//        }
//    }
//    public async Task<Result> UpdateSubscriptionPlanAsync(
//        string subscriptionId,
//        string newPriceId,
//        string prorationBehavior, // "always_invoice", "create_prorations" ou "none"
//        CancellationToken cancellationToken = default)
//    {
//        try
//        {
//            var service = new SubscriptionService(stripeClient);

//            // 1. Récupérer la souscription pour trouver l'item à remplacer
//            var currentSub = await service.GetAsync(subscriptionId, null,null, cancellationToken);
//            var subscriptionItemId = currentSub.Items.Data[0].Id; // Stripe gère des "items"

//            // 2. Mettre à jour l'item avec le nouveau prix
//            var options = new SubscriptionUpdateOptions
//            {
//                Items =
//                [
//                    new() 
//                    {
//                        Id = subscriptionItemId,
//                        Price = newPriceId, // Ton ExternalPriceId
//                    }
//                ],
//                // On utilise "none" si on a déjà prélevé le prorata manuellement avec ChargeAsync
//                ProrationBehavior = prorationBehavior
//            };

//            await service.UpdateAsync(subscriptionId, options, null, cancellationToken);
//            return Result.Success();
//        }
//        catch (StripeException ex)
//        {
//            return Result.Error(ex.Message);
//        }
//    }
    
//    public async Task<Result<PaymentResponse>> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken = default)
//    {
//        var options = new PaymentIntentCreateOptions
//        {
//            Customer = request.CustomerId,
//            Amount = (long)(request.Amount * 100), // Conversion en cents
//            Currency = request.Currency.ToLower(),
//            Confirm = true,
//            Description = request.Description,
//            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
//        };

//        var requestOptions = new RequestOptions { IdempotencyKey = request.IdempotencyKey };

//        try
//        {
//            var service = new PaymentIntentService(stripeClient);
//            var intent = await service.CreateAsync(options, requestOptions, cancellationToken);

//            return intent.Status switch
//            {
//                "succeeded" => Result.Success(new PaymentResponse(intent.Id, PaymentStatus.Succeeded)),
//                _ => Result.Error($"Payment status: {intent.Status}")
//            };
//        }
//        catch (StripeException ex)
//        {
//            return Result.Error(ex.Message);
//        }
//    }

//    public async Task<Result<string>> GetOrCreateCustomerAsync(
//    string subscriberId,
//    string email,
//    string name,
//    CancellationToken cancellationToken )
//    {
//        var service = new CustomerService(stripeClient);

//        // Recherche par metadata pour éviter les doublons (Idempotence)
//        var customers = await service.ListAsync(new CustomerListOptions
//        {
//            Metadata = new Dictionary<string, string> { { "InternalId", subscriberId } }
//        }, null, ct);

//        var existing = customers.FirstOrDefault();
//        if (existing != null) return Result.Success(existing.Id);

//        // Création si inexistant
//        var options = new CustomerCreateOptions
//        {
//            Email = email,
//            Name = name,
//            Metadata = new Dictionary<string, string> { { "InternalId", subscriberId } }
//        };
//        var customer = await service.CreateAsync(options, null, ct);
//        return Result.Success(customer.Id);
//    }


//}
