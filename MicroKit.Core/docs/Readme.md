# MicroKit.Core

Shared infrastructure implementations for all MicroKit modules. Provides the DI bootstrap entry point, default date/time providers, system clock, and JSON serializer implementations (System.Text.Json and Newtonsoft.Json).

## When to use

Install `MicroKit.Core` in every host application that uses any MicroKit module. It contains the concrete implementations for the contracts defined in `MicroKit.Abstractions`. Other MicroKit packages may depend on `MicroKit.Abstractions` only; this package wires everything together at the composition root.

## Installation

```
dotnet add package MicroKit.Core
```

## Key types

| Type | Description |
|---|---|
| `ServiceCollectionExtensions.AddMicroKit()` | Registers default infrastructure and returns `MicroKitBuilder` for fluent configuration |
| `DateTimeProvider` | `IDateTimeProvider` backed by `DateTime.UtcNow` and `DateTimeOffset.UtcNow` |
| `SystemClock` | `IClock` backed by `DateTimeOffset.UtcNow` |
| `SystemTextJsonSerializer` | `IMicroKitSerializer` using `System.Text.Json` with camelCase naming and null-value suppression |
| `NewtonsoftJsonSerializer` | `IMicroKitSerializer` using Newtonsoft.Json with camelCase, type-name handling, and reference-loop tolerance |
| `MicroKitSerializationExtensions` | Builder extensions to swap the default serializer: `AddSystemTextJson()`, `AddNewtonsoftJson()` |

## Usage

```csharp
// Program.cs
builder.Services
    .AddMicroKit()
    .AddNewtonsoftJson(); // optional: override the default System.Text.Json serializer
```

The `AddMicroKit()` call:
- Registers `IDateTimeProvider` → `DateTimeProvider` (singleton)
- Registers `IMicroKitSerializer` → `SystemTextJsonSerializer` (singleton, via `TryAdd`)
- Returns a `MicroKitBuilder` for chaining other MicroKit module registrations

## Dependencies

- `MicroKit.Abstractions`
- `Microsoft.Extensions.DependencyInjection`
- `Microsoft.Extensions.Options`
- `Newtonsoft.Json`
