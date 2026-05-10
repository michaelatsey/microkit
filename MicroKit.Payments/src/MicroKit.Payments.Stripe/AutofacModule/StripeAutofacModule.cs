using Autofac;
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

namespace MicroKit.Payments.Stripe.AutofacModule;

public sealed class StripeAutofacModule : Module
{
    private readonly IConfiguration _configuration;

    public StripeAutofacModule(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected override void Load(ContainerBuilder builder)
    {
        // 1️⃣ Bind StripeOptions via Microsoft Options
        builder.Register(ctx =>
        {
            var services = new ServiceCollection();

            services.Configure<StripeOptions>(
                _configuration.GetSection(StripeOptions.SectionName));

            var provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IOptions<StripeOptions>>();
        })
        .As<IOptions<StripeOptions>>()
        .SingleInstance();

        // 2️⃣ StripeClientFactory
        builder.RegisterType<StripeClientFactory>()
            .As<IStripeClientFactory>()
            .SingleInstance();

        // 3️⃣ IStripeClient (Scoped / InstancePerLifetimeScope)
        builder.Register(ctx =>
        {
            var options = ctx.Resolve<IOptions<StripeOptions>>().Value;
            var factory = ctx.Resolve<IStripeClientFactory>();

            return factory.Create(options.SecretKey);
        })
        .As<IStripeClient>()
        .InstancePerLifetimeScope();

        // 4️⃣ Adapters
        builder.RegisterType<StripePaymentProcessor>()
            .As<IPaymentProcessor>()
            .InstancePerLifetimeScope();

        builder.RegisterType<StripeRefundProcessor>()
            .As<IRefundProcessor>()
            .InstancePerLifetimeScope();

        builder.RegisterType<StripeCustomerGateway>()
            .As<ICustomerGateway>()
            .InstancePerLifetimeScope();

        builder.RegisterType<StripeSubscriptionProcessor>()
            .As<ISubscriptionProcessor>()
            .InstancePerLifetimeScope();
    }
}