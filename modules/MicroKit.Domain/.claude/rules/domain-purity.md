# Rule: Domain Purity — MicroKit.Domain

## Toujours actif. Règle non négociable.

## Principe
> MicroKit.Domain ne dépend de rien d'autre que le runtime .NET.
> Zéro dépendance NuGet tierce avec implémentation.
> Zéro référence aux autres modules MicroKit.

## Dépendances autorisées
```xml
<!-- UNIQUEMENT ces packages dans MicroKit.Domain.csproj -->
<PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" PrivateAssets="all" />
<!-- Pas d'autre PackageReference -->
```

## Namespaces interdits dans src/
```csharp
// ❌ BLOQUANT — tout using vers ces namespaces
using MicroKit.Result;
using MicroKit.MediatR;
using Microsoft.EntityFrameworkCore;
using MediatR;
using Serilog;
using Microsoft.AspNetCore;
using System.Net.Http;
```

## Namespaces autorisés
```csharp
// ✅ Runtime .NET uniquement
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions; // si nécessaire pour VO validation
```

## Triggers de violation (code review bloquant)
```
🔴 using MicroKit.* dans src/
🔴 PackageReference non-analyzer dans .csproj
🔴 ILogger<T> injecté dans un type de domaine
🔴 IServiceProvider dans un type de domaine
🔴 Task / async dans les méthodes de domaine (sauf exceptions justifiées)
🔴 HttpClient, DbContext, SqlConnection
```

## Note sur async dans le domaine
```
Les méthodes de domaine sont synchrones par nature.
Les agrégats ne font pas d'I/O — ils modifient l'état en mémoire.

Exception tolérée : ISpecification<T>.ToExpressionAsync() si le provider
de données le requiert — mais c'est un signe que la spec appartient
peut-être à la couche Application, pas au Domain pur.
```
