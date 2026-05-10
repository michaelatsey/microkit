using Autofac;
using MicroKit.Cqrs.Builder;

namespace MicroKit.Cqrs;

public static class AutofacExtensions
{
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