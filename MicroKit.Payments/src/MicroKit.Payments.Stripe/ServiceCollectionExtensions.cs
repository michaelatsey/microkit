using MicroKit.Payments.Abstractions;
using MicroKit.Payments.Abstractions.Builder;
using MicroKit.Payments.Stripe.Abstractions;
using MicroKit.Payments.Stripe.Customer;
using MicroKit.Payments.Stripe.Payment;
using MicroKit.Payments.Stripe.Refund;
using MicroKit.Payments.Stripe.Subscription;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stripe;

namespace MicroKit.Payments.Stripe;

public static class ServiceCollectionExtensions
{
    //public static IServiceCollection AddStripePayments(this IServiceCollection services, string apiKey)
    //{
    //    services.AddSingleton<IStripeClient>(new StripeClient(apiKey));
    //    services.AddScoped<IPaymentGateway, StripePaymentGateway>();
    //    return services;
    //}

    public static IServiceCollection AddStripePaymentGateway(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 1️⃣ Bind configuration
        services.Configure<StripeOptions>(
            configuration.GetSection(StripeOptions.SectionName));

        // 2️ Factory
        services.AddSingleton<IStripeClientFactory, StripeClientFactory>();

        // 3️ StripeClient scoped (important)
        services.AddScoped<IStripeClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<StripeOptions>>().Value;
            var factory = sp.GetRequiredService<IStripeClientFactory>();

            return factory.Create(options.SecretKey);
        });

        // Adapters
        services.AddScoped<IPaymentProcessor, StripePaymentProcessor>();
        services.AddScoped<IRefundProcessor, StripeRefundProcessor>();
        services.AddScoped<ICustomerGateway, StripeCustomerGateway>();
        services.AddScoped<ISubscriptionProcessor, StripeSubscriptionProcessor>();

        // Webhook handler
        //services.AddScoped(sp =>
        //{
        //    var options = sp.GetRequiredService<IOptions<StripeOptions>>().Value;
        //    return new StripeWebhookHandler(options.WebhookSecret);
        //});

        return services;
    }
}