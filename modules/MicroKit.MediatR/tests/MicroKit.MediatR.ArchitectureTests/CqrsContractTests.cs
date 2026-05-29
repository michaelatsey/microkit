using System.Reflection;
using MediatR;
using MicroKit.MediatR;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.ArchitectureTests;

/// <summary>
/// Verifies the CQRS contract hierarchy — ICommand, IQuery, IStreamQuery, IEvent,
/// IDomainEventNotification, and all handler interfaces are defined in Abstractions
/// and implement the correct MediatR base interfaces.
/// </summary>
public sealed class CqrsContractTests
{
    private static readonly Assembly Abstractions = typeof(ICacheableQuery).Assembly;

    // ── Contract hierarchy ─────────────────────────────────────────────────

    [Fact]
    public void ICommandGeneric_ImplementsIRequestWithTResult()
    {
        // ICommand<TResult> : IRequest<TResult>
        var iCommandGeneric = typeof(ICommand<>);
        var iRequest = typeof(IRequest<>);

        iCommandGeneric.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == iRequest)
            .ShouldBeTrue("ICommand<TResult> must implement IRequest<TResult>");
    }

    [Fact]
    public void IQuery_ImplementsIRequest()
    {
        // IQuery<TResult> : IRequest<TResult>
        var iQuery = typeof(IQuery<>);
        var iRequest = typeof(IRequest<>);

        iQuery.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == iRequest)
            .ShouldBeTrue("IQuery<TResult> must implement IRequest<TResult>");
    }

    [Fact]
    public void IStreamQuery_ImplementsIStreamRequest()
    {
        // IStreamQuery<TResult> : IStreamRequest<TResult>
        var iStreamQuery = typeof(IStreamQuery<>);
        var iStreamRequest = typeof(IStreamRequest<>);

        iStreamQuery.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == iStreamRequest)
            .ShouldBeTrue("IStreamQuery<TResult> must implement IStreamRequest<TResult>");
    }

    [Fact]
    public void IDomainEventNotification_ImplementsINotification()
    {
        // IDomainEventNotification<TEvent> : INotification
        var iNotification = typeof(INotification);

        typeof(IDomainEventNotification<>)
            .GetInterfaces()
            .ShouldContain(iNotification,
                "IDomainEventNotification<TEvent> must implement MediatR.INotification for fan-out dispatch");
    }

    // ── Contract placement in Abstractions ─────────────────────────────────

    [Fact]
    public void CqrsContracts_AreDefinedInAbstractions()
    {
        var cqrsContracts = new[]
        {
            typeof(ICommand),
            typeof(ICommand<>),
            typeof(IQuery<>),
            typeof(IStreamQuery<>),
            typeof(IEvent),
            typeof(IDomainEventNotification<>),
        };

        foreach (var contract in cqrsContracts)
        {
            contract.Assembly.ShouldBe(Abstractions,
                $"{contract.Name} must be defined in MicroKit.MediatR.Abstractions, not in Core or Behaviors");
        }
    }

    [Fact]
    public void HandlerInterfaces_AreDefinedInAbstractions()
    {
        var handlerInterfaces = new[]
        {
            typeof(ICommandHandler<>),
            typeof(ICommandHandler<,>),
            typeof(IQueryHandler<,>),
            typeof(IStreamQueryHandler<,>),
            typeof(IDomainEventHandler<,>),
        };

        foreach (var iface in handlerInterfaces)
        {
            iface.Assembly.ShouldBe(Abstractions,
                $"{iface.Name} must be defined in MicroKit.MediatR.Abstractions");
        }
    }
}
