using Autofac;
using MicroKit.Cqrs.Builder;

namespace MicroKit.Cqrs;

/// <summary>Autofac registration extensions for MicroKit CQRS.</summary>
public static class AutofacExtensions
{
    /// <summary>Registers all MicroKit CQRS services into the Autofac <paramref name="builder"/>.</summary>
    /// <param name="builder">The Autofac container builder.</param>
    /// <param name="configure">Optional delegate to customise CQRS registration options.</param>
    /// <returns>The same <paramref name="builder"/> instance for fluent chaining.</returns>
    public static ContainerBuilder AddMicroKitCqrs(
        this ContainerBuilder builder,  
        Action<MicroKitCqrsBuilder>? configure = null)
    {
        var innerBuilder = new MicroKitCqrsBuilder(builder);

        // Configuration personnalisée
        configure?.Invoke(innerBuilder);
        
        // On s'assure que le cache est configuré si l'utilisateur ne l'a pas fait manuellement
        innerBuilder.Build();

        return builder;
    }
}