# MicroKit.Cqrs.MediatR.Autofac

Autofac registration module for the full MediatR CQRS pipeline. Scans assemblies for handlers, registers `ICommandBus` and `IQueryBus`, wires FluentValidation validators, and applies ordered pipeline behaviors — all via a single fluent call.

## When to use

Use this package when your application uses Autofac as its DI container. If you use Microsoft DI, register `MediatRCommandBus` and `MediatRQueryBus` as `ICommandBus` and `IQueryBus` manually and call `services.AddMediatR()` directly.

## Installation

```
dotnet add package MicroKit.Cqrs.MediatR.Autofac
```

## Key types

| Type | Description |
|---|---|
| `CqrsMediatRAutofacExtension.UseMediatRModule()` | Extension on `MicroKitCqrsBuilder`; entry point for Autofac MediatR wiring |
| `CqrsMediatRBuilder` | Fluent builder for registering handler assemblies and pipeline behaviors |
| `CqrsMediatRBuilder.AddPipeline<T>(int order)` | Registers an `IPipelineBehavior<,>` type at an explicit pipeline position |

## Usage

```csharp
// Program.cs or Autofac module
var builder = new ContainerBuilder();

builder
    .AddMicroKitCqrs(options => options.RegisterAssembly(typeof(CreateOrderHandler).Assembly))
    .UseMediatRModule(mediatR =>
    {
        // Pipeline behaviors run in ascending order
        mediatR.AddPipeline<LoggingBehavior<,>>(order: 10);
        mediatR.AddPipeline<ValidationBehavior<,>>(order: 20);
        mediatR.AddPipeline<TransactionBehavior<,>>(order: 30);
    });
```

The `MediatRCoreModule` registered internally:
1. Calls `RegisterMediatR` with all provided assemblies and open-generic handler registration.
2. Registers `MediatRCommandBus` as `ICommandBus` (per-lifetime-scope).
3. Registers `MediatRQueryBus` as `IQueryBus` (per-lifetime-scope).
4. Scans assemblies for `IValidator<>` implementations.
5. Registers pipeline behaviors in the specified order.

## Dependencies

- `Autofac`
- `MediatR.Extensions.Autofac.DependencyInjection`
- `FluentValidation`
- `MicroKit.Cqrs.MediatR`
- `MicroKit.Cqrs.MediatR.Abstractions`
- `MicroKit.Cqrs`
