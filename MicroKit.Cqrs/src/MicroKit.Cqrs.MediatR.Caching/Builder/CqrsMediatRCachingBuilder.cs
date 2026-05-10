//using Autofac;
//using MicroKit.Cqrs.MediatR.Abstractions;
//using MicroKit.Cqrs.MediatR.Caching.Modules;

//namespace MicroKit.Cqrs.MediatR.Caching.Builder;

//public class CqrsMediatRCachingBuilder
//{
//    public ContainerBuilder Builder { get; }
//    // Optimise avec un cache
//    private readonly List<PipelineRegistration> _registrations = [];

//    public CqrsMediatRCachingBuilder(ContainerBuilder builder) => Builder = builder;

//    // On permet de définir l'ordre explicitement
//    public CqrsMediatRCachingBuilder AddPipeline(Type type, int order = 0)
//    {
//        _registrations.Add(new PipelineRegistration(type, order));
//        return this;
//    }

//    internal void Build()
//    {
//        // On trie ici pour obtenir le IOrderedEnumerable
//        var orderedPipelines = _registrations
//            .OrderBy(x => x.Order)
//            .Select(x => x.Type);

//        Builder.RegisterModule(new CqrsMediatRChachingPipelineModule(orderedPipelines));
//    }

//}
