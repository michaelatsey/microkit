# Hook: pre-commit — MicroKit.Messaging

Runs before every commit touching `modules/MicroKit.Messaging/`.

## Checks

### 1. Build gate — 0 errors, 0 warnings

```bash
dotnet build modules/MicroKit.Messaging/MicroKit.Messaging.slnx -c Release --no-incremental 2>&1 \
  | grep -E "^.+ (error|warning) [A-Z]+[0-9]+"
```

Exit non-zero if any compiler diagnostic line appears (format: `file(line,col): error CS####`).
`TreatWarningsAsErrors=true` in Release config means a single warning is a build failure.

> The pattern `grep -E "error|warning"` is **avoided** — it produces false positives on MSBuild
> informational lines containing those words. The `^.+ (error|warning) [A-Z]+[0-9]+` pattern
> matches only actual compiler/MSBuild diagnostic lines.

### 2. No MediatR.Contracts reference

```bash
grep -rn "MediatR\.Contracts" modules/MicroKit.Messaging/ \
  --include="*.csproj" --include="*.cs" --include="*.props"
```

Exit non-zero if any match found. **This is a zero-tolerance check** — `MediatR.Contracts` must
never appear anywhere in this module, in any form (PackageReference, using directive, type reference).

### 3. No Console.WriteLine

```bash
grep -rn "Console\.Write" modules/MicroKit.Messaging/src/ --include="*.cs"
```

Exit non-zero if any match found. Use `ILogger<T>` throughout.

### 4. XML docs on public API

```bash
dotnet build modules/MicroKit.Messaging/MicroKit.Messaging.slnx -c Release \
  -p:GenerateDocumentationFile=true 2>&1 | grep "CS1591"
```

Exit non-zero if any `CS1591` (missing XML comment) warnings appear. All public members in
`src/` projects must be documented.

### 5. No FluentAssertions

```bash
grep -rn "FluentAssertions\|\.Should()\." modules/MicroKit.Messaging/ --include="*.cs" --include="*.csproj"
```

Exit non-zero if any match found. Shouldly is mandatory; FluentAssertions is banned (Xceed EULA).

## Scope

This hook applies **only to files under `modules/MicroKit.Messaging/`**. It must not run on
changes to other modules.
