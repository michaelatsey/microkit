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
using System.Text.Json;               // uniquement dans ValueObjects/Common/Json/ (convertisseurs)
using System.Text.Json.Serialization; // [JsonConverter] sur Money, Currency, Actor
```

> **Pourquoi `System.Text.Json` est autorisé (#167, ADR-003 D7).** Un événement de domaine passe
> par l'outbox : ses value objects doivent faire l'aller-retour JSON. `System.Text.Json` fait partie
> du framework partagé, donc aucun package n'est ajouté.
>
> - `Money`, `Currency` et un `Actor` utilisateur font l'aller-retour avec les options par défaut.
> - Un acteur intégré (`$system`, `$anonymous`, `$migration`) **ne** le fait **pas**. Le
>   convertisseur par défaut est asymétrique (#181, ADR-004 D1) : il écrit tout acteur, intégrés
>   compris, mais refuse le préfixe `$` à la lecture (#170). Un acteur intégré écrit avec les
>   options par défaut ne se relit donc qu'avec `TrustedActorJsonConverter` enregistré dans les
>   `JsonSerializerOptions.Converters` du stockage que l'application a écrit elle-même (outbox,
>   event store, cache), jamais pour un corps de requête.
> - Tant que l'outbox Messaging ne peut pas enregistrer ce convertisseur (#174), un événement qui
>   doit porter un acteur intégré en porte la `Value` en `string`, que le consommateur relit avec
>   `Actor.Restore`.
>
> Seuls des `[JsonConverter]` et des convertisseurs `internal` sont permis, plus un seul type public :
> `TrustedActorJsonConverter` (#170), cet opt-in explicite. Aucune autre API de sérialisation.

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
