namespace MicroKit.Messaging;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

/// <summary>Registers <see cref="MessagingOptions"/>: binding, precedence, validation and the forwards.</summary>
internal static class MessagingOptionsSetup
{
    /// <summary>
    /// Registers <see cref="MessagingOptions"/>, once per service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="composition">The composition whose builders carry <c>bus.ServiceName</c>.</param>
    /// <remarks>
    /// <para>
    /// <b>Precedence, lowest first</b>: <c>IHostEnvironment.ApplicationName</c>, configuration
    /// (<c>MicroKit:Messaging</c>), <c>bus.Configure(...)</c>, <c>bus.ServiceName</c>. The binding
    /// is the first <c>Configure</c> step, so every <c>bus.Configure</c> — registered later, by any
    /// <c>AddMicroKitMessaging</c> call — overrides it. <c>bus.ServiceName</c> and the host's name
    /// apply in one <c>PostConfigure</c>, which runs after every <c>Configure</c> whatever the call
    /// order, and reads the builders only when the options are first resolved — so a name set on
    /// a builder after <c>AddMicroKitMessaging</c> returned still counts.
    /// </para>
    /// <para>
    /// A blank service name counts as absent at every level: an empty configuration value falls
    /// back to the next source, as does a blank one set through <c>bus.Configure</c>.
    /// </para>
    /// <para>
    /// <b>Not <c>BindConfiguration</c></b>, deliberately: it resolves <see cref="IConfiguration"/>
    /// with <c>GetRequiredService</c>, so a container composed by hand — a bare
    /// <c>ServiceCollection</c> — could not resolve the options at all. Reading it through
    /// <c>GetService</c> binds when configuration exists and keeps the defaults when it does not.
    /// The <c>Bind</c> call is intercepted by the configuration binding source generator
    /// (<c>EnableConfigurationBindingGenerator</c>), so no reflection-based binder runs. No change
    /// token is registered: configuration is read once, when the options are first resolved.
    /// </para>
    /// <para>
    /// The processors keep their own option types, registered as forwards of
    /// <see cref="MessagingOptions.Outbox"/> and <see cref="MessagingOptions.Inbox"/>; resolving
    /// either validates the whole <see cref="MessagingOptions"/> first.
    /// </para>
    /// </remarks>
    public static void Register(IServiceCollection services, MessagingComposition composition)
    {
        var options = services.AddOptions<MessagingOptions>();

        options.Configure<IServiceProvider>(static (o, provider) =>
            provider.GetService<IConfiguration>()?.GetSection(MessagingOptions.SectionName).Bind(o));

        options.PostConfigure<IServiceProvider>((o, provider) =>
            ApplyServiceName(o, composition, provider.GetService<IHostEnvironment>()));

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MessagingOptions>, MessagingOptionsValidator>());
        options.ValidateOnStart();

        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<MessagingOptions>>().Value.Outbox);
        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<MessagingOptions>>().Value.Inbox);
    }

    /// <summary>Applies the two outer levels of the service-name precedence.</summary>
    /// <param name="options">The options, after configuration and every <c>bus.Configure</c>.</param>
    /// <param name="composition">The composition whose builders carry <c>bus.ServiceName</c>.</param>
    /// <param name="environment">The host environment, when there is one.</param>
    internal static void ApplyServiceName(
        MessagingOptions options, MessagingComposition composition, IHostEnvironment? environment)
    {
        // Two different names are a composition error, reported at start by the analysis; the
        // first one is applied meanwhile so nothing else fails on a missing name.
        var names = composition.ServiceNames;
        if (names.Count > 0)
        {
            options.ServiceName = names[0];
            return;
        }

        if (string.IsNullOrWhiteSpace(options.ServiceName))
        {
            options.ServiceName = string.IsNullOrWhiteSpace(environment?.ApplicationName)
                ? null
                : environment.ApplicationName;
        }
    }
}
