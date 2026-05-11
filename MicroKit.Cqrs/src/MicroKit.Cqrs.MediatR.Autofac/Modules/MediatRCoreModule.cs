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

internal sealed class MediatRCoreModule : Module
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
        // 1. Register MediatR — scans assemblies for IRequestHandler<,> and INotificationHandler<>
        var configuration = MediatRConfigurationBuilder
            .Create("", [.. _assemblies])
            .WithAllOpenGenericHandlerTypesRegistered()
            .Build();
        builder.RegisterMediatR(configuration);

        // 2. Register buses
        builder.RegisterType<MediatRCommandBus>().As<ICommandBus>().InstancePerLifetimeScope();
        builder.RegisterType<MediatRQueryBus>().As<IQueryBus>().InstancePerLifetimeScope();

        // 3. FluentValidation validators
        builder.RegisterAssemblyTypes(_assemblies)
            .Where(t => t.IsClosedTypeOf(typeof(IValidator<>)))
            .AsImplementedInterfaces()
            .InstancePerLifetimeScope();

        // 4. MediatR pipeline behaviors (ordered by caller)
        foreach (var pipeline in _middlePipelines)
        {
            builder.RegisterGeneric(pipeline)
                .As(typeof(IPipelineBehavior<,>))
                .InstancePerLifetimeScope();
        }
    }
}



