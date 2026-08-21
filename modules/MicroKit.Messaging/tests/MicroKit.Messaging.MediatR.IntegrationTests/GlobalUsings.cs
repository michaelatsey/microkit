// NOTE: MicroKit.Domain.Events and MicroKit.MediatR.Events BOTH declare an `IEvent`
// (the latter is an [Obsolete] shim onto the former). A bare `IEvent` is therefore ambiguous
// in this assembly — always qualify it, or reference IDomainEvent / IIntegrationEvent instead.
global using MediatR;
global using MicroKit.Domain.Events;
global using MicroKit.MediatR.DependencyInjection;
global using MicroKit.MediatR.Events;
global using MicroKit.MediatR.Extensions;
global using MicroKit.MediatR.Handlers;
global using MicroKit.MediatR.Requests;
global using MicroKit.Messaging.EntityFrameworkCore;
global using MicroKit.Messaging.MediatR.IntegrationTests.Fixtures;
global using MicroKit.Messaging.MediatR.IntegrationTests.Infrastructure;
global using MicroKit.Result;
global using Microsoft.Data.Sqlite;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.DependencyInjection;
global using Shouldly;
global using Xunit;
global using Xunit.Abstractions;   // ITestOutputHelper (xunit v2)
