# Issues convention — MicroKit

Scope: the `michaelatsey/microkit` monorepo.

## 1. Where things live

| A future reader wants to… | It lives in |
|---|---|
| act on something owed | a GitHub issue |
| understand why a decision was taken | an ADR |
| know what changed and why | the commit message and the PR that closed the issue |

GitHub Issues is the only tracker of owed work. There are no session traces and no
`LOT.md`: an issue carries both the finding and, once decided, the spec.

Issues are written in English.

## 2. Title

```
[<module>] <type>: <imperative description>
```

`<module>` and `<type>` are the label suffixes below, verbatim. One module per issue; an
indivisible change goes under the module that owns the contract change. Lower case, no
trailing period.

```
[messaging] defect: four worker sites still catch InvalidOperationException
[domain] break: remove the service locator from IUnitOfWork
[monorepo] chore: replace the nine release workflows with a manifest-driven one
[mediatr] audit: assess owed debt before stable
```

## 3. Labels

| Family | Labels | Rule |
|---|---|---|
| Module | `mod:auth` `mod:domain` `mod:execution-abstractions` `mod:logging` `mod:mediatr` `mod:messaging` `mod:persistence` `mod:result` `mod:tenancy` `mod:monorepo` | Exactly one. `mod:monorepo` covers root, `eng/`, `.github/`, tooling, versioning. Caching, Http and Observability get a label when they enter the publishable perimeter (ADR-GLOBAL-002 D11). |
| Type | `type:break` `type:defect` `type:feature` `type:chore` `type:docs` `type:audit` | Exactly one. `audit` is a bounded investigation that produces issues, not code. |
| Blocking | `blocks:tag` `blocks:stable` | At most one. Every `type:break` carries at least `blocks:stable` (ADR-GLOBAL-002 D10). |
| Decision | `needs:decision` | The finding is known, the solution is not. Removed before work starts, never during. |

## 4. Body

At creation, the finding only — never a solution:

```markdown
## Finding
What is wrong, quoted from the file, with path:line.

## Consequence
What it causes for a consumer or a maintainer.

## Source
The ADR, review or session that surfaced it.
```

Once the solution is decided, and before any code, the issue gains a spec:

```markdown
## Spec
Scope: files and interfaces that change.
Out of scope: what must not be touched; anything found there becomes a new issue.
Verification: the commands to run and the result that proves the fix.
```

## 5. Lifecycle

**Ready** — one `mod:*`, one `type:*`, a finding with references, a `## Spec`, no
`needs:decision`.

**Done** — for any type that changes the repository: one branch, one PR to `dev`, closed
by `Closes #N`, verification green. For `audit` and for work not planned: closed by hand
with a comment linking the issues it produced or stating why.

Anything found and not fixed gets its own issue before the PR is merged.

## 6. Creating the labels

`--force` updates a label that already exists instead of failing.

```bash
for m in auth domain execution-abstractions logging mediatr messaging \
         persistence result tenancy monorepo; do
  gh label create "mod:$m" --color 0E8A16 --description "Module $m" --force
done

gh label create "type:break"   --color B60205 --description "Public surface break" --force
gh label create "type:defect"  --color D93F0B --description "Known incorrect behaviour" --force
gh label create "type:feature" --color 1D76DB --description "Missing capability" --force
gh label create "type:chore"   --color C5DEF5 --description "Tooling, CI, versioning" --force
gh label create "type:docs"    --color 0075CA --description "Wrong or missing documentation" --force
gh label create "type:audit"   --color 5319E7 --description "Bounded investigation" --force

gh label create "blocks:tag"    --color 000000 --description "Blocks any tag (ADR-GLOBAL-002)" --force
gh label create "blocks:stable" --color 5A5A5A --description "Blocks 2.0.0 stable" --force

gh label create "needs:decision" --color FBCA04 --description "Solution not decided" --force
```
