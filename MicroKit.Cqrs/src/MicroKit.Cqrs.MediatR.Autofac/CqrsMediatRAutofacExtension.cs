using MicroKit.Cqrs.Builder;
using MicroKit.Cqrs.MediatR.Autofac.Builder;

namespace MicroKit.Cqrs.MediatR.Autofac;

/// <summary>Autofac extension methods for registering the MediatR CQRS pipeline.</summary>
public static class CqrsMediatRAutofacExtension
{
    /// <summary>Registers the MediatR module into the Autofac container via the CQRS builder.</summary>
    /// <param name="configuration">The CQRS builder.</param>
    /// <param name="mediatRBuilder">Optional callback to further configure the MediatR pipeline.</param>
    /// <returns>The configured <see cref="CqrsMediatRBuilder"/>.</returns>
    public static CqrsMediatRBuilder UseMediatRModule(this MicroKitCqrsBuilder configuration,
        Action<CqrsMediatRBuilder>? mediatRBuilder = null )
    {
        var assemblies = configuration.Options.Assemblies;
        CqrsMediatRBuilder innerBuilder = new(configuration.Builder, assemblies);
        mediatRBuilder?.Invoke(innerBuilder);

        innerBuilder.Build();

        return innerBuilder;
    }
}
