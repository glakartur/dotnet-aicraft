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
- **`overrides`** (member-level) — given a virtual/abstract member, find its overrides up/down the
  inheritance chain. Correctness gap (grep can't resolve overrides). Deferred follow-up split off
  from the `hierarchy` brainstorm, which is scoped to type→type lineage only; see
  [docs/brainstorms/2026-06-25-hierarchy-command-requirements.md](docs/brainstorms/2026-06-25-hierarchy-command-requirements.md).
  Open: lands as its own command vs. an `impls` extension (`impls` already covers abstract
  member→implementations).

## Tranche 4 — codebase-health / graph analysis (exploratory)

Whole-codebase structural insight, framed as *"the tools cover … saving roughly 10x tokens on
navigation"*. Most of these read the symbol + project graph Roslyn already holds, so they fit the
**efficiency** criterion (agent never reads whole files to reconstruct the graph). Each needs an
ADR-0001 sanity check before it earns a slot — some items below already have a home, and one
collides with the data-source boundary.

- **anti-pattern detection** — flag known structural smells from the semantic model (god classes,
  feature envy, leaky abstractions, public-mutable-static, etc.). New; in-scope if rules stay
  symbol-graph-derived, not heuristic text scanning.
- **circular dependency detection** — cycles in the namespace / type / project reference graph.
  New; squarely in-scope (pure graph query grep cannot do).
- **dependency graph visualization** — emit the project/type dependency graph in a machine-readable
  form (DOT/JSON) the agent can reason over. New; in-scope as a render of the `projects` graph.
- **dead code analysis** — already partially covered (the skill surfaces dead code / unused
  symbols today); a dedicated whole-solution sweep could be the explicit command.
- **symbol resolution, type hierarchies, project graphs** — already shipped or planned: see
  `definition`/`refs`/`impls` (T1), `hierarchy` (T2), `projects` (T3). No new slot needed.
- **test coverage mapping** — ⚠️ conflicts with **Out of scope** below: coverage is runtime/execution
  data per ADR-0001. A *static* test→symbol map (which tests reference which symbols, via `refs`
  from test assemblies) would be in-scope; line/branch coverage would not. Needs a decision.

## Out of scope (per ADR-0001)

NuGet metadata, coverage, runtime/execution data — different kind of tool.
