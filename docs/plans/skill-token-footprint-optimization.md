# Plan: Reduce `dotnet-aicraft` skill token footprint

**Status:** proposed
**Author:** Artur Gawrylak (with Claude)
**Trigger:** Claude Code usage summary flagged `/dotnet-aicraft:dotnet-aicraft` as ~15% of session token usage, with the hint: *"Heavy skills can be scoped down or run with a cheaper model via skill frontmatter."*
**Scope:** `plugins/dotnet-aicraft/skills/dotnet-aicraft/` — `SKILL.md` (body + frontmatter `description`). References stay as-is.

---

## 1. Problem

In a .NET-heavy repo the skill triggers on essentially every symbol-level task (its own `description` says *"LOAD EAGERLY"*, and consumer projects add a hard CLAUDE.md rule: *"load … as the first step"*). Once triggered, the full `SKILL.md` body is injected and then persists in the transcript across many turns — so its cost compounds. That body is the dominant contributor to the 15%.

The root issue is **broken progressive disclosure**: the body re-implements the reference files instead of pointing at them.

## 2. Measured token breakdown

Word counts (tokens ≈ words × 1.33):

| Layer | Size | Loaded when | Nature |
|---|---|---|---|
| `description` (frontmatter) | 265 words (~350 tok) | **every session**, unconditionally | always-on overhead |
| **`SKILL.md` body** | **1636 words (~2200 tok)** | **every skill trigger**, persists in transcript | **primary cost** |
| `references/commands.md` | 2062 words (~2750 tok) | on-demand `Read` | fine as-is |
| `references/patterns.md` | 884 words (~1180 tok) | on-demand `Read` | fine as-is |

## 3. Root cause — body duplicates the references

The body inlines content that already lives, in full, in the on-demand reference files:

| Body section (lines, current `SKILL.md`) | Duplicated in | Action |
|---|---|---|
| `## Commands` — per-command examples + JSON schemas for all 8 commands (~165–287) | `references/commands.md` (complete) | **Move out** — keep only a selection table |
| `## Output Format` + `{ solutionRoot, items }` envelope explanation (~104–120) | `commands.md` "Path conventions" + per-command schemas | **Collapse** to 2 lines |
| `## Shared Options` (~138–153) | `commands.md` "Global Options" | **Collapse** to a pointer |
| `## Identifying Symbols` (~156–163) | `commands.md` "Symbol Name Format" | **Collapse** to a pointer |
| `## Agent Workflows` (~289–311) | `references/patterns.md` (complete) | **Move out** |
| `## When to Use Proactively` table (~122–135) | overlaps "Never Use Text Search" table (~43–54) | **Merge** into one table |

Net effect: ~2200 tokens are paid on every trigger for content that is also sitting in files loaded only when needed.

## 4. Planned changes

### 4.1 Body: ~280 → ~90 lines (target saving ~1700–1800 tok/trigger, ≈ −55–60%)

Keep only the tier that genuinely must be present the moment the skill triggers:

- One paragraph: what the tool is (Roslyn daemon, ~50ms, auto-start/idle-shutdown).
- **One merged table** combining "don't grep — use X" with "situation → command". This is the skill's value proposition and stays.
- One-liner: **FQN-first** workflow (`symbols --pattern` → use the `fullName` field in follow-ups). The single non-obvious gotcha.
- One-liner: **solution auto-discovery** (`-s` optional from a single-solution folder) + the three error codes (`SOLUTION_AMBIGUOUS`, `SOLUTION_NOT_FOUND`, `CONFLICTING_PATH_ARGUMENTS`) with one-clause remedies.
- Pointers: exact flags + output schemas → `references/commands.md`; workflows + decision trees → `references/patterns.md`; `dotnet aicraft <cmd> --help` is the source of truth for flags.

Everything else (per-command sections, JSON schemas, shared-options table, identifying-symbols block, agent workflows) is **removed from the body** — it is already, in full, in the references.

Secondary benefit: with a good selection table in the body, the agent stops reflexively `Read`-ing `commands.md` and only opens it when it actually needs an exact schema — a further saving not captured in the per-trigger number above.

### 4.2 Description: 265 → ~90 words (target saving ~220 tok in *every* session)

The `description` is a triggering signal, not a manual. Trim to: what it is + the eager-load condition (".NET file in scope → before grep/Glob/Read") + a short trigger-phrase cluster.

Remove:
- the 8-bullet operations list (reference material, not a trigger signal),
- the 10-situation "Load PROACTIVELY when" list (redundant with the trigger phrases),
- the closing "ALWAYS prefer …" paragraph (already covered, and consumer projects restate it in CLAUDE.md).

Triggering is double-anchored (skill description **and** the consumer CLAUDE.md rule), so a leaner description is low-risk here. If extra confidence is wanted, validate trigger rate with the skill-creator `run_loop.py` description-optimizer (~20 should/should-not-trigger queries).

### 4.3 Model frontmatter — explicitly NOT applied

The usage hint offers running the skill on a cheaper model. **Reject for this skill.** `dotnet-aicraft` is an *inline* skill: the main thread runs the CLI and then reasons about `refs`/`callers`/rename blast-radius itself. There is no separate sub-context to downgrade — a cheaper model would degrade exactly the refactor-safety reasoning that must stay sharp. The "scope down" lever (§4.1–4.2) is the correct one; "cheaper model" fits subagent-style skills, not driver skills.

## 5. Proposed slim body skeleton

```markdown
# dotnet-aicraft

Semantic .NET analysis via Roslyn — compiler precision, not text search. A background
daemon loads the solution once (~50ms/query), auto-starts on first use, idles out after 60 min.

## Use this, not text search

| Question | Command |
|---|---|
| Where is X used? / what calls it? | `refs` / `callers --symbol "FQN"` |
| Where is X declared? (from a usage) | `definition --file --line --col` |
| What implements this interface? | `impls --symbol "FQN"` |
| Is this dead/unused? safe to delete? | `unused` + `refs` |
| Rename a symbol safely | `rename --dry-run` then `rename` |
| Find a symbol by (partial) name | `symbols --pattern "Foo*"` |
| Compiler errors before editing | `diagnostics --severity error` |

grep/Glob/Read **miss**: renamed locals, interface dispatch, virtual/override calls,
extension methods, generics, partial classes, XML-doc refs. Roslyn finds all of them.

## Two things to know

- **FQN-first.** Most commands want a fully-qualified name. If you only have a short name,
  run `symbols --pattern "Name*"` and use the `fullName` from the result in follow-ups.
- **Solution auto-discovery.** `-s` is optional from a folder with exactly one
  `.slnx`/`.sln`/`.csproj`. On `SOLUTION_AMBIGUOUS` pass `-s <path>`; on `SOLUTION_NOT_FOUND`
  `cd` into (or pass) a folder with one; on `CONFLICTING_PATH_ARGUMENTS` drop one of `-s`/`-p`.

## More detail

- Exact flags + output schemas → `references/commands.md`
- Workflow patterns + decision trees → `references/patterns.md`
- `dotnet aicraft <command> --help` — authoritative, current flags
```

## 6. Proposed slim description

```yaml
description: >
  Roslyn-powered semantic code intelligence for .NET solutions via the `dotnet aicraft` CLI:
  compiler-precise references, call graphs, implementations, definitions, safe renames, dead-code
  and diagnostics. A background daemon answers in ~50ms.

  Use whenever .NET/C#/F#/VB.NET code is in scope (any .sln/.csproj/.cs/.vb/.fs visible or
  mentioned) — reach for it BEFORE grep/Glob/ripgrep/Read on any symbol-level question, since
  text search misses interface dispatch, overrides, extension methods, generics and partial
  classes. Triggers: "find references/usages", "who calls X", "go to definition",
  "what implements X", "rename symbol", "is this dead code", "find unused", "compiler errors".
```

## 7. Risk & validation

- **Risk:** trimming the description under-triggers the skill. **Mitigation:** triggering is double-anchored (description + consumer CLAUDE.md rule); optionally confirm with `run_loop.py`.
- **Risk:** the body loses a detail the agent relied on. **Mitigation:** nothing is deleted — everything removed from the body already exists verbatim in the references; the body keeps the value-prop table + the two real gotchas.
- **Optional measurement:** run a handful of representative symbol tasks through the skill-creator harness before/after; `benchmark.json` records `total_tokens` per run, giving a real before/after delta rather than an estimate.

## 8. Acceptance criteria

- [ ] `SKILL.md` body ≤ ~100 lines; no per-command JSON schema or shared-options table remains in the body.
- [ ] `description` ≤ ~110 words; no operations bullet-list, no "load proactively" list.
- [ ] `references/commands.md` and `references/patterns.md` unchanged (still hold full detail).
- [ ] Spot-check: each of the 8 commands is still reachable from the body via the selection table + reference pointers.
- [ ] (Optional) measured per-trigger token cost down ≥ 50%.
```
