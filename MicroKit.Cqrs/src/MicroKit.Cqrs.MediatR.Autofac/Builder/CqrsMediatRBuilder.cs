using Autofac;
using MicroKit.Cqrs.MediatR.Abstractions;
using MicroKit.Cqrs.MediatR.Autofac.Modules;
using System.Reflection;

namespace MicroKit.Cqrs.MediatR.Autofac.Builder;

/// <summary>Builder for configuring the MediatR CQRS pipeline within an Autofac container.</summary>
public class CqrsMediatRBuilder
{
    private readonly HashSet<Assembly> _assemblies = [];

    /// <summary>Gets the registered handler assemblies as a read-only view.</summary>
    public IReadOnlySet<Assembly> Assemblies => _assemblies;

    private readonly List<PipelineRegistration> _pipelines = [];

    /// <summary>Gets the Autofac container builder.</summary>
    public ContainerBuilder Builder { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="CqrsMediatRBuilder"/>.
    /// </summary>
    /// <param name="builder">The Autofac container builder.</param>
    /// <param name="assemblies">Optional seed assemblies to scan for handlers.</param>
    public CqrsMediatRBuilder(ContainerBuilder builder, HashSet<Assembly>? assemblies = null)
    {
        Builder = builder;
        if (assemblies is not null)
            foreach (var a in assemblies) _assemblies.Add(a);
    }

    /// <summary>Registers a pipeline behavior type at the given order position.</summary>
    public void AddPipeline<T>(int order) => _pipelines.Add(new(typeof(T), order));

    internal CqrsMediatRBuilder Build()
    {
        var finalPipelines = new List<PipelineRegistration>(_pipelines);
        var ordered = finalPipelines.OrderBy(p => p.Order).Select(p => p.Type);
        Builder.RegisterModule(new MediatRCoreModule(ordered, [.. _assemblies]));
        return this;
    }
}
