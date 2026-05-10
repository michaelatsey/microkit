Bien sûr 😄 — voici une version **professionnelle du README.md en français**, adaptée pour un package NuGet MicroKit.ApiVersioning.

---

# MicroKit.ApiVersioning

> Gestion avancée des versions d’API pour ASP.NET Core (.NET 9 / .NET 10)
> Conçu pour les microservices, Clean Architecture, DDD et systèmes modulaires.

---

## 🚀 Présentation

**MicroKit.ApiVersioning** fournit une couche d’intégration propre et validée sur [`Asp.Versioning`](https://github.com/dotnet/aspnet-api-versioning) pour :

* ✅ Minimal APIs
* ✅ Controllers
* ✅ Swagger / OpenAPI
* ✅ Clean Architecture
* ✅ Intégration Autofac
* ✅ Microservices
* ✅ Validation de configuration au démarrage
* ✅ Comportement “fail-fast” pour la production

Elle impose une configuration fortement typée, élimine les valeurs par défaut silencieuses et garantit un démarrage sûr pour les systèmes distribués.

---

## 📦 Installation

```bash
dotnet add package MicroKit.ApiVersioning
```

Si vous utilisez Autofac :

```bash
dotnet add package MicroKit.ApiVersioning.Autofac
```

---

## ⚙️ Configuration

Ajouter la section suivante dans `appsettings.json` :

```json
{
  "ApiVersioning": {
    "DefaultVersion": "1.0",
    "HeaderKey": "X-Api-Version",
    "QueryStringReaderKey": "api-version",
    "MediaTypeReaderKey": "ver",
    "GroupNameFormat": "'v'VVV",
    "AssumeDefaultVersionWhenUnspecified": true,
    "ReportApiVersions": true,
    "SubstituteApiVersionInUrl": true
  }
}
```

---

## 🧠 Fonctionnalités

* Options fortement typées
* Validation au démarrage (`ValidateOnStart`)
* Validation métier avancée via `IValidateOptions<T>`
* Lecteurs de version configurables :

  * Segment d’URL
  * Query string
  * Header
  * Media type
* Aucune valeur par défaut silencieuse
* Séparation claire DI Microsoft ↔ Autofac
* Abstraction OpenAPI via `IOpenApiDescriptor`

---

## 🏗 Enregistrement dans Microsoft DI

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMicroKitApiVersioning(builder.Configuration);
```

C’est tout ! ✅

---

## Exemple Minimal API

```csharp
var app = builder.Build();

app.MapGet("/v{version:apiVersion}/hello", () => "Bonjour !")
   .WithApiVersionSet()
   .MapToApiVersion(1.0);

app.Run();
```

---

## Exemple Controller

```csharp
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public class ProductsController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok("Produits v1");
}
```

---

## 🧩 Intégration Autofac

MicroKit fournit un module Autofac propre, **sans couplage avec `IServiceCollection`**.

```csharp
using Autofac;
using Autofac.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

builder.Services.AddMicroKitApiVersioning(builder.Configuration);

builder.Host.ConfigureContainer<ContainerBuilder>(container =>
{
    container.RegisterModule<MicroKitVersioningModule>();
});
```

✅ Pas besoin de `Populate()`.
✅ Respect total des bonnes pratiques Autofac.

---

## 🔎 Support OpenAPI / Swagger

Le package expose :

```csharp
public interface IOpenApiDescriptor
{
    string HeaderName { get; }
    string DefaultVersion { get; }
}
```

Vous pouvez l’injecter dans vos filtres ou services :

```csharp
public class SwaggerHeaderFilter
{
    private readonly IOpenApiDescriptor _descriptor;

    public SwaggerHeaderFilter(IOpenApiDescriptor descriptor)
    {
        _descriptor = descriptor;
    }
}
```

---

## 🛡 Validation de configuration

Le package utilise :

* `ValidateOnStart()`
* `IValidateOptions<T>` pour la validation métier
* Validation du format de version API

Si la configuration est invalide, l’application **échoue au démarrage** :

```
Options validation failed for 'MicroKitApiVersioningOptions':
- DefaultVersion doit être une version API valide (ex: 1.0)
```

Parfait pour :

* Docker / Kubernetes
* CI/CD
* Environnements de production

---

## 🧱 Principes d’architecture

* Clean Architecture
* Respect des principes SOLID
* Aucune fuite de framework
* Pas de fallback silencieux
* Testable et modulaire

---

## 🔬 Tests

Vous pouvez tester facilement vos options :

```csharp
var options = Options.Create(new MicroKitApiVersioningOptions
{
    DefaultVersion = "1.0",
    HeaderKey = "X-Api-Version",
    QueryStringReaderKey = "api-version",
    MediaTypeReaderKey = "ver",
    GroupNameFormat = "'v'VVV"
});
```

Ou tester la validation via `IValidateOptions`.

---

## 🌍 Conçu pour les microservices

Idéal pour :

* API Gateways
* Microservices DDD
* Monolithes modulaires
* Déploiements multi-environnements
* Plateformes d’entreprise

---

## 🔮 Roadmap

* Support multi-tenant
* Automation Swagger multi-version
* Provider de version dynamique runtime
* Helpers OpenAPI UI

---

## 📌 Prérequis

* .NET 9 ou .NET 10
* [Asp.Versioning](https://github.com/dotnet/aspnet-api-versioning)

---

## 📜 Licence

MIT

---

## 🤝 Contribution

Les PR sont bienvenues.
Les discussions architecturales encouragées.

---

## 🧠 Philosophie

> Fail fast.
> Valider tôt.
> Éviter les valeurs par défaut silencieuses.
> Séparer les conteneurs DI.
> Construire d’abord pour la production.

---

Si tu veux, je peux maintenant te rédiger **la section Multi-Tenant avancée** et **Swagger multi-version** pour ce README en français, prête à mettre directement dans le package NuGet.

Veux-tu que je fasse ça ?
