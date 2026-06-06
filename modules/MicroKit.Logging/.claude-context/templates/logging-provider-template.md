# Template: Logging-Provider

Code template for a new logging provider integration project.

Used by `/logging-new-provider` command. Replace all `{Placeholder}` values.

---

## File: `MicroKit.Logging.{ProviderName}.csproj`

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
    <Description>MicroKit.Logging integration for {ProviderName}.</Description>
    <PackageTags>logging;microkit;observability;{providertag}</PackageTags>
    <PackageProjectUrl>https://github.com/michaelatsey/microkit</PackageProjectUrl>
    <RepositoryUrl>https://github.com/michaelatsey/microkit</RepositoryUrl>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  </PropertyGroup>

  <ItemGroup>
    <!-- Internal project references — NO Version= attribute -->
    <ProjectReference Include="..\..\MicroKit.Logging.Abstractions\MicroKit.Logging.Abstractions.csproj" />
    <ProjectReference Include="..\..\MicroKit.Logging\MicroKit.Logging.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- Provider SDK — version in Directory.Packages.props -->
    <PackageReference Include="{ProviderSdkPackage}" />
  </ItemGroup>

</Project>
```

---

## File: `LoggingBuilderExtensions.cs`

```csharp
namespace MicroKit.Logging.{ProviderName};

/// <summary>
/// Extension methods for integrating MicroKit.Logging with {ProviderName}.
/// </summary>
public static class LoggingBuilderExtensions
{
    /// <summary>
    /// Adds MicroKit.Logging integration for {ProviderName}.
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to configure.</param>
    /// <returns>The <paramref name="builder"/> for chaining.</returns>
    public static ILoggingBuilder AddMicroKit{ProviderName}(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<ILogEnricher, {ProviderName}LogEnricher>();
        // Register additional services here

        return builder;
    }
}
```

---

## File: `{ProviderName}LogEnricher.cs`

```csharp
namespace MicroKit.Logging.{ProviderName};

/// <summary>
/// Enriches log entries with {ProviderName}-specific context.
/// </summary>
public sealed class {ProviderName}LogEnricher : ILogEnricher
{
    /// <inheritdoc />
    public void Enrich(IEnrichmentContext context)
    {
        if (!context.IsEnabled)
        {
            return;
        }

        // Add provider-specific properties using LogPropertyNames constants
        // context.Properties[LogPropertyNames.XxxId] = ...;
    }
}
```
