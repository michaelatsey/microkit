---
paths:
  - "**/tests/**"
  - "**/*Tests/**"
  - "**/testing/**"
  - "Directory.Packages.props"
---

# Testing

- xUnit, Shouldly, NSubstitute, NetArchTest
- Testcontainers PostgreSQL for anything touching uniqueness or concurrency.
- SQLite integration tests: each `Task.Run` has its own isolated connection.
- Recording fakes over `DidNotReceive()`.
- A fix comes with a test shown to fail when the fix is reverted.
- ArchitectureTests are required before any release; an empty ArchitectureTests project blocks it.
- Test projects under `tests/` set `GenerateDocumentationFile=false` and
  `NoWarn CS1591;CA1707`.
- Analyzer tests built on Microsoft.CodeAnalysis.Testing may use a bare Xunit.Assert; do not
  convert them to Shouldly.

## FluentAssertions is forbidden

Version 8.x of FluentAssertions moved to a commercial licence (Xceed Software EULA), which
requires a paid subscription for any organisation above USD 1M annual revenue or for any use in
a commercial product. Shouldly is the only assertion library: MIT licence,
https://github.com/shouldly/shouldly.

Forbidden in every `.csproj` and in `Directory.Packages.props`:

```xml
<PackageReference Include="FluentAssertions" />
<PackageVersion Include="FluentAssertions" Version="..." />
```

Required — declared in `Directory.Packages.props`, referenced by every test project:

```xml
<PackageVersion Include="Shouldly" Version="4.x.x" />
<PackageReference Include="Shouldly" />
```

Detection:

```bash
grep -r "FluentAssertions" modules/ --include="*.csproj" --include="*.cs" -l
grep -r "\.Should()\." modules/ --include="*.cs" -l
```
