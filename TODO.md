# TODO — command backlog

Ideas for new AI-facing commands, ordered by priority. Selection criterion and data-source
boundary are fixed in [docs/adr/0001](docs/adr/0001-tool-identity-and-data-source-scope.md);
verb meanings are in [CONTEXT.md](CONTEXT.md). A capability earns a slot if it improves
**correctness** (grep is wrong) **or** **efficiency** (saves the agent reading whole files).

## Tranche 1 — efficiency/navigation ✅ shipped (v0.10.0)

Shipped per [docs/plans/2026-06-07-001-feat-symbol-inspection-commands-plan.md](docs/plans/2026-06-07-001-feat-symbol-inspection-commands-plan.md).

- **`outline`** — declared members of a container (`--file` or `--symbol <type>`) as flat located
  lines, no bodies. `private` shown by default. `--public-only` keeps the extensible surface;
  `--include-inherited` walks the base-class chain, grouping inherited members by declaring type,
  suppressing overridden members and tagging `new`-shadowed ones. Full nesting, source order.
- **`source`** — verbatim text of a symbol's full declaration (XML-doc + attributes + signature +
  body) with `file`/`startLine`/`endLine`. `partial` symbols and overloads return one block per
  part. Metadata-only / generated symbols degrade to a non-error "no source available" note.
- **`describe`** — semantic card: superset of `definition` + signature, return/parameter types,
  modifiers, attributes, cleaned XML-doc, and sibling overloads (excluding the target). For a type:
  base/interfaces/type-params in the signature, **no** member list (that's `outline`).

## Tranche 2 — correctness gaps

Lower frequency than T1, but cases where grep returns a *wrong* answer and no current command
covers them.

- **`hierarchy`** — base types + derived types of a class, transitive, across projects, generics
  aware (`--direction up|down`). `impls` only covers interface→implementations today.
- **`refs --write-only`** — references to a field/property filtered to *write* sites. Roslyn
  classifies read vs write; grep cannot.

## Tranche 3 — MSBuild

- **`projects`** — project / reference / package / TFM graph from the already-loaded workspace
  (transitive references that reading a single `.csproj` won't reveal).

## Later / maybe

- **find-by-shape** — semantic queries grep can't express: symbols by attribute (`[Obsolete]`,
  `[HttpPost]`), by signature (returns `Task<T>`, implements `IDisposable` but not `sealed`).
- **refactorings** — `change-signature`, `move-type`, `extract-method`/`inline`. High value for
  *safe edits* but expensive: transactional, dry-run + apply, file-watcher conflicts.
- **`change-namespace`** — file-level namespace move/sync; see paused brainstorm
  (`brainstorms/aicraft-change-namespace`). Decisions locked, paused on engine choice.

## Out of scope (per ADR-0001)

NuGet metadata, coverage, runtime/execution data — different kind of tool.
