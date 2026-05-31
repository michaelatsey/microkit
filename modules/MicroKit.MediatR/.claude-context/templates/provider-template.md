# Template: Provider

Code template for an optional integration/provider project. Used by `/new-provider`.
Replace all `{Placeholder}` values.

---

## File: `MicroKit.MediatR.{ProviderName}.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <!-- NuGet metadata -->
  <PropertyGroup>
    <Authors>Ange-Michaël Atsé</Authors>
    <Description>MicroKit.MediatR integration for {ProviderName}.</Description>
    <PackageTags>mediatr;cqrs;microkit;{providertag};dotnet</PackageTags>
    <PackageProjectUrl>https://github.com/michaelatsey/microkit</PackageProjectUrl>
    <RepositoryUrl>https://github.com/michaelatsey/microkit</RepositoryUrl>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  </PropertyGroup>

  <ItemGroup>
    <!-- Internal project references — NO Version= attribute -->
    <ProjectReference Include="..\MicroKit.MediatR.Abstractions\MicroKit.MediatR.Abstractions.csproj" />
    <ProjectReference Include="..\MicroKit.MediatR\MicroKit.MediatR.csproj" />
    <!-- Add ..\MicroKit.MediatR.Behaviors\... ONLY if this provider backs a behavior -->
  </ItemGroup>

  <ItemGroup>
    <!-- Provider SDK — version in Directory.Packages.props -->
    <PackageReference Include="{ProviderSdkPackage}" />
  </ItemGroup>

</Project>
```

---

## File: `ServiceCollectionExtensions.cs`

```csharp
namespace MicroKit.MediatR.{ProviderName};

/// <summary>Extension methods integrating MicroKit.MediatR with {ProviderName}.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Adds the MicroKit.MediatR integration for {ProviderName}.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddMicroKitMediatR{ProviderName}(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register the adapter — delegate all logic to it, none here.
        services.AddSingleton<I{Abstraction}, {ProviderName}{Abstraction}>();

        return services;
    }
}
```

---

## File: `{ProviderName}{Abstraction}.cs`

```csharp
namespace MicroKit.MediatR.{ProviderName};

/// <summary>{ProviderName} adapter for {what it bridges}.</summary>
public sealed class {ProviderName}{Abstraction}(/* SDK deps */) : I{Abstraction}
{
    /// <inheritdoc />
    public async ValueTask {Method}(/* ... */, CancellationToken ct = default)
    {
        // Bridge to the provider SDK. ConfigureAwait(false) on every await.
    }
}
```

---

## Smoke Test (IntegrationTests — Shouldly)

```csharp
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.{ProviderName}.IntegrationTests;

public sealed class RegistrationTests
{
    [Fact]
    public void AddMicroKitMediatR{ProviderName}_RegistersAdapter()
    {
        var provider = new ServiceCollection()
            .AddMicroKitMediatR{ProviderName}()
            .BuildServiceProvider();

        provider.GetService<I{Abstraction}>().ShouldNotBeNull();
    }
}
```

## Constraints

- References `MicroKit.MediatR.Abstractions` + core only (+ `Behaviors` only if backing a behavior) — never another provider
- No `Version=` on `PackageReference`
- `sealed` adapter; `ConfigureAwait(false)` everywhere; XML docs on all public members
- Tests use Shouldly + NSubstitute — never FluentAssertions
