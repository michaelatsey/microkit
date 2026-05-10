using Autofac;
using FluentValidation;
using MediatR;
using MediatR.Extensions.Autofac.DependencyInjection;
using MediatR.Extensions.Autofac.DependencyInjection.Builder;
using MicroKit.Cqrs.Abstractions.Commands;
using MicroKit.Cqrs.Abstractions.Queries;
using MicroKit.Cqrs.MediatR.Commands;
using MicroKit.Cqrs.MediatR.Queries;
using System.Reflection;
using Module = Autofac.Module;

namespace MicroKit.Cqrs.MediatR.Autofac.Modules;

internal class MediatRCoreModule : Module
{
    private readonly Assembly[] _assemblies;
    private readonly IEnumerable<Type> _middlePipelines;
    public MediatRCoreModule(IEnumerable<Type> middlePipelines, params Assembly[] assemblies)
    {
        _middlePipelines = middlePipelines;
        _assemblies = assemblies;
    }

    protected override void Load(ContainerBuilder builder)
    {
        // 1. Enregistrement de MediatR
        var configuration = MediatRConfigurationBuilder
            .Create("", [.. _assemblies])
            .WithAllOpenGenericHandlerTypesRegistered()
            .Build();
        builder.RegisterMediatR(configuration);

        // 2. Enregistrement de nos Bus
        builder.RegisterType<MediatRCommandBus>().As<ICommandBus>().InstancePerLifetimeScope();
        builder.RegisterType<MediatRQueryBus>().As<IQueryBus>().InstancePerLifetimeScope();

        // 3. Scan des Handlers personnalisés (si non fait par MediatR)
        builder.RegisterAssemblyTypes(_assemblies)
            .AsClosedTypesOf(typeof(ICommandHandler<>))
            .AsClosedTypesOf(typeof(ICommandHandler<,>))
            .AsClosedTypesOf(typeof(IQueryHandler<,>))
            .InstancePerLifetimeScope();

        // -------------------------------------------------
        // FluentValidation
        // -------------------------------------------------
        builder.RegisterAssemblyTypes(_assemblies)
            .Where(t => t.IsClosedTypeOf(typeof(IValidator<>)))
            .AsImplementedInterfaces()
            .InstancePerLifetimeScope();

        // 2. DYNAMIC REGISTRATION (Les autres modules)
        foreach (var pipeline in _middlePipelines)
        {
            builder.RegisterGeneric(pipeline)
                .As(typeof(IPipelineBehavior<,>)).InstancePerLifetimeScope();
        }
    }

    
}



