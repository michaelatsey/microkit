using Autofac;
using MicroKit.Cqrs.MediatR.Abstractions;
using MicroKit.Cqrs.MediatR.Autofac.Modules;
using System.Reflection;

namespace MicroKit.Cqrs.MediatR.Autofac.Builder;

public class CqrsMediatRBuilder
{
    public HashSet<Assembly> Assemblies { get; private set; } = [];
    private readonly List<PipelineRegistration> _pipelines = [];
    public ContainerBuilder Builder { get; }

    public CqrsMediatRBuilder(ContainerBuilder builder, HashSet<Assembly>? assemblies = null)
    {
        Builder = builder;
        Assemblies = assemblies ?? [];
    }

    public void AddPipeline<T>(int order) => _pipelines.Add(new(typeof(T), order));

    internal CqrsMediatRBuilder Build()
    {
        var finalPipelines = new List<PipelineRegistration>(_pipelines);
        // Tri unique et enregistrement
        var ordered = finalPipelines.OrderBy(p => p.Order).Select(p => p.Type);
        Builder.RegisterModule(new MediatRCoreModule(ordered, [.. Assemblies]));
        return this;
    }

}
