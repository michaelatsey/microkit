# Rule: Documentation — MicroKit.Persistence

## XML Documentation (src/ projects)

Every public type and member in `src/` carries XML documentation. `GenerateDocumentationFile`
is on and `TreatWarningsAsErrors` applies in Release (root `Directory.Build.props`), so a missing
`<summary>` fails a Release build (CS1591).

## Minimum Tags

| Member | Tags |
|--------|------|
| Interface / class / record | `<summary>`; `<typeparam>` on generics |
| Method | `<summary>` + `<param>` + `<returns>` (non-void) |
| `IUnitOfWork.CommitAsync`, `ITransactionalContext.ExecuteAsync` | + `<exception cref="PersistenceException">` |
| Exception constructors | `<summary>` + `<param>` |
| Implementation of a documented contract member | `<inheritdoc/>` |

## Style

- `<summary>` states what the member does, in the present: "Finds an aggregate by its identifier."
- Cross-reference related contracts with `<see cref=""/>`.
- Say what a consumer must know and cannot see in the signature: tracking, what is loaded,
  retries and their at-least-once consequence, what an exception means.

## README and samples

`README.md` is the consumer documentation. Every C# sample it shows lives in
`samples/MicroKit.Persistence.Samples` and builds in Release with the analyzers enabled; change
both together.
