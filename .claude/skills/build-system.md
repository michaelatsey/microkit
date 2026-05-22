# Skill: Build System — MicroKit Monorepo

## Quand activer ce skill
- Modification de Directory.Build.props ou Directory.Packages.props
- Ajout d'un nouveau package NuGet tiers
- Problème de build inter-modules
- Configuration d'un nouveau projet .csproj

## Architecture du build

### Directory.Build.props (build/ — appliqué à tous les projets)
```xml
<Project>
  <PropertyGroup>
    <!-- Langue et framework -->
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- Qualité -->
    <TreatWarningsAsErrors Condition="'$(Configuration)' == 'Release'">true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>

    <!-- NuGet metadata partagée -->
    <Authors>MicroKit Contributors</Authors>
    <RepositoryUrl>https://github.com/[org]/MicroKit</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageIcon>icon.png</PackageIcon>
    <PackageReadmeFile>README.md</PackageReadmeFile>

    <!-- Documentation XML -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn><!-- Allow missing XML on private members -->

    <!-- NativeAOT / Trimming -->
    <IsTrimmable>true</IsTrimmable>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  </PropertyGroup>

  <!-- Analyzers partagés -->
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" PrivateAssets="all" />
    <PackageReference Include="Roslynator.Analyzers" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

### Directory.Packages.props (build/ — NuGet Central Package Management)
```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup Label="Framework">
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Caching.Abstractions" Version="9.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Options" Version="9.0.0" />
  </ItemGroup>

  <ItemGroup Label="MediatR">
    <PackageVersion Include="MediatR" Version="12.*" />
    <PackageVersion Include="MediatR.Extensions.Microsoft.DependencyInjection" Version="12.*" />
  </ItemGroup>

  <ItemGroup Label="Validation">
    <PackageVersion Include="FluentValidation" Version="12.*" />
    <PackageVersion Include="FluentValidation.DependencyInjectionExtensions" Version="12.*" />
  </ItemGroup>

  <ItemGroup Label="Resilience">
    <PackageVersion Include="Polly" Version="8.*" />
    <PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="9.*" />
  </ItemGroup>

  <ItemGroup Label="Serialization">
    <PackageVersion Include="System.Text.Json" Version="9.0.0" />
  </ItemGroup>

  <ItemGroup Label="Observability">
    <PackageVersion Include="OpenTelemetry" Version="1.*" />
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.*" />
  </ItemGroup>

  <ItemGroup Label="Testing" Condition="false">
    <!-- Pas de condition — les projets de test filtrent via leur ItemGroup -->
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageVersion Include="xunit" Version="2.*" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageVersion Include="FluentAssertions" Version="7.*" />
    <PackageVersion Include="NSubstitute" Version="5.*" />
    <PackageVersion Include="Bogus" Version="35.*" />
    <PackageVersion Include="BenchmarkDotNet" Version="0.14.*" />
    <PackageVersion Include="NetArchTest.Rules" Version="1.*" />
    <PackageVersion Include="coverlet.collector" Version="6.*" />
  </ItemGroup>

  <ItemGroup Label="Analyzers">
    <PackageVersion Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="9.*" />
    <PackageVersion Include="Roslynator.Analyzers" Version="4.*" />
  </ItemGroup>
</Project>
```

## Règles d'ajout de packages

```
1. Toujours ajouter dans Directory.Packages.props (jamais de version dans les .csproj)
2. Utiliser des ranges de version sémantique (8.* = minor et patch flexibles)
3. Les analyzers/dev tools → PrivateAssets="all" dans le .csproj qui les consomme
4. Les packages de test → uniquement dans les projets de test
5. Avant d'ajouter un package tiers → vérifier s'il n'est pas déjà dans le fichier central
```

## Commandes build utiles

```bash
# Build tout le monorepo
dotnet build MicroKit.slnx

# Build un seul module
dotnet build modules/MicroKit.Result/MicroKit.Result.slnx

# Tests avec coverage
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage

# Pack un module (sans publier)
dotnet pack modules/MicroKit.Result/src/MicroKit.Result/MicroKit.Result.csproj -c Release -o ./nupkg

# Vérifier les packages vulnérables
dotnet list package --vulnerable --include-transitive

# Vérifier les packages outdated
dotnet list package --outdated
```

## global.json (fixe la version SDK)
```json
{
  "sdk": {
    "version": "9.0.100",
    "rollForward": "latestMinor"
  }
}
```
