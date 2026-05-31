# Rule: Testing Libraries — MicroKit

## Toujours actif pour tout projet de test dans le monorepo.

## Bibliothèque d'assertions obligatoire : Shouldly

### Décision
**Shouldly (MIT)** est la seule bibliothèque d'assertions autorisée dans MicroKit.
**FluentAssertions est interdit** — sa version 8.x a introduit une licence commerciale
(Xceed Software EULA) qui exige un abonnement payant pour toute organisation générant
plus de 1 M USD de revenus annuels ou pour tout usage dans des produits commerciaux.

### Références
- Shouldly : https://github.com/shouldly/shouldly — MIT License
- FluentAssertions 8.x licensing : https://xceed.com/products/unit-testing/

---

## Règles

### ❌ Interdit — dans tout fichier `.csproj` et `Directory.Packages.props`
```xml
<!-- JAMAIS dans MicroKit -->
<PackageReference Include="FluentAssertions" />
<PackageVersion Include="FluentAssertions" Version="..." />
```

### ✅ Obligatoire — déclaration dans `Directory.Packages.props`
```xml
<PackageVersion Include="Shouldly" Version="4.x.x" />
```

### ✅ Obligatoire — référence dans les projets de test
```xml
<PackageReference Include="Shouldly" />
```

---

## Syntaxe Shouldly — équivalents FluentAssertions

### Égalité et valeurs

| FluentAssertions | Shouldly |
|---|---|
| `result.Should().Be(expected)` | `result.ShouldBe(expected)` |
| `result.Should().NotBe(unexpected)` | `result.ShouldNotBe(unexpected)` |
| `result.Should().BeNull()` | `result.ShouldBeNull()` |
| `result.Should().NotBeNull()` | `result.ShouldNotBeNull()` |
| `result.Should().BeTrue()` | `result.ShouldBeTrue()` |
| `result.Should().BeFalse()` | `result.ShouldBeFalse()` |
| `result.Should().BeEquivalentTo(expected)` | `result.ShouldBeEquivalentTo(expected)` |

### Types

| FluentAssertions | Shouldly |
|---|---|
| `result.Should().BeOfType<T>()` | `result.ShouldBeOfType<T>()` |
| `result.Should().BeAssignableTo<T>()` | `result.ShouldBeAssignableTo<T>()` |
| `result.Should().NotBeOfType<T>()` | `result.ShouldNotBeOfType<T>()` |

### Chaînes

| FluentAssertions | Shouldly |
|---|---|
| `str.Should().Contain("sub")` | `str.ShouldContain("sub")` |
| `str.Should().NotContain("sub")` | `str.ShouldNotContain("sub")` |
| `str.Should().StartWith("prefix")` | `str.ShouldStartWith("prefix")` |
| `str.Should().EndWith("suffix")` | `str.ShouldEndWith("suffix")` |
| `str.Should().BeNullOrEmpty()` | `str.ShouldBeNullOrEmpty()` |
| `str.Should().NotBeNullOrEmpty()` | `str.ShouldNotBeNullOrEmpty()` |
| `str.Should().HaveLength(n)` | `str.Length.ShouldBe(n)` |
| `str.Should().Match("pattern")` | `str.ShouldMatch("pattern")` |

### Comparaisons numériques

| FluentAssertions | Shouldly |
|---|---|
| `value.Should().BeGreaterThan(x)` | `value.ShouldBeGreaterThan(x)` |
| `value.Should().BeGreaterThanOrEqualTo(x)` | `value.ShouldBeGreaterThanOrEqualTo(x)` |
| `value.Should().BeLessThan(x)` | `value.ShouldBeLessThan(x)` |
| `value.Should().BeLessThanOrEqualTo(x)` | `value.ShouldBeLessThanOrEqualTo(x)` |
| `value.Should().BeInRange(min, max)` | `value.ShouldBeInRange(min, max)` |

### Collections

| FluentAssertions | Shouldly |
|---|---|
| `col.Should().BeEmpty()` | `col.ShouldBeEmpty()` |
| `col.Should().NotBeEmpty()` | `col.ShouldNotBeEmpty()` |
| `col.Should().Contain(item)` | `col.ShouldContain(item)` |
| `col.Should().NotContain(item)` | `col.ShouldNotContain(item)` |
| `col.Should().HaveCount(n)` | `col.Count.ShouldBe(n)` |
| `col.Should().Contain(x => predicate)` | `col.ShouldContain(x => predicate)` |
| `col.Should().OnlyContain(x => predicate)` | `col.ShouldAllBe(x => predicate)` |
| `col.Should().BeInAscendingOrder()` | `col.ShouldBeInOrder()` |

### Exceptions synchrones

| FluentAssertions | Shouldly |
|---|---|
| `action.Should().Throw<TEx>()` | `Should.Throw<TEx>(() => action())` |
| `action.Should().NotThrow()` | `Should.NotThrow(() => action())` |
| `action.Should().Throw<TEx>().WithMessage("msg")` | `Should.Throw<TEx>(() => action()).Message.ShouldContain("msg")` |

```csharp
// ✅ Shouldly — exception synchrone
var ex = Should.Throw<InvalidOperationException>(() => sut.Execute());
ex.Message.ShouldContain("expected message");
```

### Exceptions asynchrones

| FluentAssertions | Shouldly |
|---|---|
| `await action.Should().ThrowAsync<TEx>()` | `await Should.ThrowAsync<TEx>(async () => await action())` |
| `await action.Should().NotThrowAsync()` | `await Should.NotThrowAsync(async () => await action())` |

```csharp
// ✅ Shouldly — exception asynchrone
var ex = await Should.ThrowAsync<InvalidOperationException>(
    async () => await sut.ExecuteAsync());
ex.Message.ShouldContain("expected message");
```

### Assertions personnalisées avec message d'erreur

```csharp
// Shouldly supporte un message personnalisé en dernier paramètre
result.ShouldBe(expected, "because the happy path must always succeed");
result.ShouldNotBeNull("result must be set after calling Execute()");
```

---

## Exemples complets

### Test unitaire typique
```csharp
// ✅ Bonne pratique MicroKit
[Fact]
public void Map_WhenSuccess_TransformsValue()
{
    var result = Result.Ok(42).Map(x => x * 2);

    result.IsSuccess.ShouldBeTrue();
    result.Value.ShouldBe(84);
}

[Fact]
public void Map_WhenFailure_PropagatesError()
{
    var error = Error.From("not found");
    var result = Result.Fail<int>(error).Map(x => x * 2);

    result.IsFailure.ShouldBeTrue();
    result.Error.ShouldBe(error);
}
```

### Test avec collection
```csharp
// ✅
[Fact]
public void GetErrors_ReturnsAllErrors()
{
    var errors = sut.GetErrors();

    errors.ShouldNotBeEmpty();
    errors.Count.ShouldBe(2);
    errors.ShouldContain(e => e.Code == "VALIDATION_ERROR");
}
```

### Test d'exception
```csharp
// ✅
[Fact]
public void Execute_WhenNotInitialized_Throws()
{
    var ex = Should.Throw<InvalidOperationException>(() => sut.Execute());
    ex.Message.ShouldContain("not initialized");
}

[Fact]
public async Task ExecuteAsync_WhenCancelled_ThrowsOperationCancelled()
{
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await Should.ThrowAsync<OperationCanceledException>(
        async () => await sut.ExecuteAsync(cts.Token));
}
```

---

## Détection des violations

Signaux d'alerte déclenchant une revue obligatoire :
```
🔴 Toute référence à FluentAssertions dans un .csproj ou Directory.Packages.props
🔴 using FluentAssertions; dans un fichier de test
🔴 .Should(). dans le code de test (syntaxe FluentAssertions)
🟡 Autre bibliothèque d'assertions non listée ici (Xunit.Assert seul est toléré
   pour les cas sans Shouldly dans les analyzer tests basés sur Microsoft.CodeAnalysis.Testing)
```

### Commande de vérification rapide
```bash
# Détecter toute utilisation de FluentAssertions dans le monorepo
grep -r "FluentAssertions" modules/ --include="*.csproj" --include="*.cs" -l
grep -r "\.Should()\." modules/ --include="*.cs" -l
```
