using MicroKit.Cqrs.Builder;
using MicroKit.Cqrs.MediatR.Autofac.Builder;

namespace MicroKit.Cqrs.MediatR.Autofac;

public static class CqrsMediatRAutofacExtension
{
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
