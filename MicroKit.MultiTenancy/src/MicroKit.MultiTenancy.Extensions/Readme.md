# Utilisation

```csharp
builder.Services
    .AddMicroKitMultiTenancy()
    .WithCompositeTenantRegionResolver(
        typeof(ClaimsTenantRegionResolver),
        typeof(DatabaseTenantRegionResolver),
        typeof(DefaultTenantRegionResolver));
```

# Enregistrement dans la DI (Program.cs)
Dans ton application finale, tu lieras l'interface à ton implémentation :

```csharp
//Enregistrement de la stratégie de détection des tenants
services.AddScoped<ITenantRegistry, EfTenantRegistry>();
```