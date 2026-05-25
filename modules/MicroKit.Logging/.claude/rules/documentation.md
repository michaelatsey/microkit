# Rule: Documentation

## XML Documentation (src/ projects only)

All public types and members in `src/` projects require XML documentation.

```xml
<GenerateDocumentationFile>true</GenerateDocumentationFile>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

A missing `<summary>` on a public member is a build error in Release configuration.

### Minimum Required Tags

| Member type | Required tags |
|-------------|--------------|
| Interface | `<summary>` |
| Interface method | `<summary>`, `<param>` for each param, `<returns>` |
| Class | `<summary>` |
| Public method | `<summary>`, `<param>`, `<returns>` |
| `CancellationToken ct` | `<param name="ct">Propagates notification that operations should be cancelled.</param>` |
| Property | `<summary>` |
| Constant | `<summary>` |

### Style

- `<summary>` uses imperative mood: "Gets the current operation context." not "This property gets..."
- Avoid restating the obvious: `/// <summary>The tenant ID.</summary>` is sufficient for `TenantId`
- Cross-reference with `<see cref=""/>` where relevant
