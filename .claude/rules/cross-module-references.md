# Rule: Cross-Module References — MicroKit

## Toujours actif pour tout fichier `.csproj` qui référence un autre module MicroKit.

---

## Canonical pattern — deux ItemGroups obligatoires

Tout `.csproj` qui dépend d'un autre module MicroKit DOIT utiliser exactement ce pattern :

```xml
<!-- DEV: source ProjectReferences — CI/Release: published NuGet packages -->
<!-- ⚠ Any new cross-module dependency must be added to BOTH ItemGroups -->
<ItemGroup Condition="'$(CIReleaseBuild)' != 'true'">
  <ProjectReference Include="[relative path to module src]" />
</ItemGroup>
<ItemGroup Condition="'$(CIReleaseBuild)' == 'true'">
  <PackageReference Include="[PackageName]" />
</ItemGroup>
```

### Exemple concret — MicroKit.MediatR.Abstractions
```xml
<!-- DEV: source ProjectReferences — CI/Release: published NuGet packages -->
<!-- ⚠ Any new cross-module dependency must be added to BOTH ItemGroups -->
<ItemGroup Condition="'$(CIReleaseBuild)' != 'true'">
  <ProjectReference Include="../../../MicroKit.Result/src/MicroKit.Result/MicroKit.Result.csproj" />
  <ProjectReference Include="../../../MicroKit.Domain/src/MicroKit.Domain/MicroKit.Domain.csproj" />
</ItemGroup>
<ItemGroup Condition="'$(CIReleaseBuild)' == 'true'">
  <PackageReference Include="MicroKit.Result" />
  <PackageReference Include="MicroKit.Domain" />
</ItemGroup>
```

---

## Règles — non négociables

### ❌ INTERDIT — condition sur un item individuel

```xml
<!-- ❌ BLOQUANT — condition sur ProjectReference individuel -->
<ProjectReference Include="../../../MicroKit.Result/src/MicroKit.Result/MicroKit.Result.csproj"
                  Condition="'$(CIReleaseBuild)' != 'true'" />
<PackageReference Include="MicroKit.Result"
                  Condition="'$(CIReleaseBuild)' == 'true'" />

<!-- ❌ BLOQUANT — condition sur PackageReference individuel -->
<PackageReference Include="MicroKit.Logging.Abstractions"
                  Condition="'$(CIReleaseBuild)' == 'true'" />
```

La condition doit être sur le `<ItemGroup>`, jamais sur les items individuels.
Des conditions sur items individuels dispersées dans le fichier rendent le graphe de
dépendances illisible et cassent la symétrie requise.

### ✅ Règles obligatoires

1. **Toujours deux `ItemGroup`** — un pour chaque mode de build
   - `Condition="'$(CIReleaseBuild)' != 'true'"` → ProjectReferences (source locale)
   - `Condition="'$(CIReleaseBuild)' == 'true'"` → PackageReferences (NuGet publié)

2. **Toujours les deux commentaires avertisseurs** au-dessus des deux `ItemGroup` :
   ```xml
   <!-- DEV: source ProjectReferences — CI/Release: published NuGet packages -->
   <!-- ⚠ Any new cross-module dependency must be added to BOTH ItemGroups -->
   ```

3. **Symétrie stricte** — chaque `ProjectReference` dans le premier `ItemGroup` doit avoir
   un `PackageReference` correspondant dans le second, et vice versa. L'asymétrie est un bug.

4. **Chemins relatifs uniquement** — les `ProjectReference` utilisent des chemins relatifs,
   jamais des chemins absolus.

5. **CPM obligatoire** — les versions dans les `PackageReference` sont gérées par
   `Directory.Packages.props` (Central Package Management). Jamais de `Version=` hardcodé.

6. **Projets de test exclus** — les projets avec `IsPackable=false` (`IsPublishable=false`)
   ne sont jamais packagés. Leurs `ProjectReference` cross-module sont inconditionnels :
   ```xml
   <!-- ✅ Test projects — always unconditional, never packed -->
   <ProjectReference Include="../../../MicroKit.Result/src/MicroKit.Result/MicroKit.Result.csproj" />
   ```

7. **Solution .slnx** — tout module référencé via `ProjectReference` (dans le bloc
   `CIReleaseBuild != 'true'`) DOIT apparaître dans le `.slnx` correspondant, afin que
   `dotnet restore` le résolve correctement.

---

## Anti-patterns à bloquer

| Anti-pattern | Pourquoi c'est un bug |
|---|---|
| `Condition=` sur un `ProjectReference` individuel | Asymétrie cachée, difficile à auditer |
| `Condition=` sur un `PackageReference` individuel | Idem |
| `ProjectReference` sans `PackageReference` jumeau | Build pack cassé (`CIReleaseBuild=true`) |
| `PackageReference` sans `ProjectReference` jumeau | Build dev/CI cassé (`CIReleaseBuild=false`) |
| `Version=` dans un `PackageReference` cross-module | Violation CPM |
| Chemin absolu dans `ProjectReference` | Non portable entre environnements |
| Module référencé mais absent du `.slnx` | `dotnet restore` échoue en CI |

---

## Checklist pour la dependency-guardian

Avant tout merge touchant un `.csproj`, vérifier :

```
☐ Les refs cross-module sont dans deux ItemGroup conditionnels (pas sur items individuels)
☐ Les deux commentaires avertisseurs sont présents au-dessus des ItemGroups
☐ Symétrie parfaite : même nombre d'entrées dans les deux ItemGroups
☐ Chaque ProjectReference a un PackageReference jumeau avec le bon nom de package
☐ Aucun Version= dans les PackageReference (CPM uniquement)
☐ Tous les chemins ProjectReference sont relatifs et resolvent correctement
☐ Tout nouveau module référencé est ajouté au .slnx
☐ Les projets test (IsPackable=false) ont des ProjectReference inconditionnels
```

---

## Enforcement

- **`dependency-guardian`** doit vérifier ce pattern sur toute review de `.csproj`
- Toute déviation est un **BLOQUANT** — pas de merge sans correction
- La détection peut être automatisée : chercher `Condition=` sur des `<ProjectReference>` ou
  `<PackageReference>` individuels dans un `.csproj` est toujours une violation
