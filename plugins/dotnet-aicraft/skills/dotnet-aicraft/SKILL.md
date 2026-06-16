---
name: dotnet-aicraft
description: >
  Compiler-grade exploration, understanding and safe edits for .NET/C#/F#/VB.NET via Roslyn —
  accurate where text search silently fails (interface dispatch, overrides, extension methods,
  generics, partial classes), and far cheaper than reading whole files or guessing at BCL/NuGet
  types with no source on disk. Use it to:
  - explore and understand unfamiliar .NET code — what's there, how it's structured, how it connects
  - read a method's body or any symbol's source, and see a class's structure (its members and their
    signatures), without opening the file; also inspect a symbol's exact signature, types and XML-doc
  - find every real reference / caller / implementation / override; jump to a definition
  - rename safely without breaking the build; surface dead code and compiler errors

  STOP before you locate, search, trace or read .NET code by any text- or file-based means — a Bash
  command (`grep -r`, `find`, `cat`/`head`, `ls`) or a tool (Search, Grep, Glob, Read). This is their
  drop-in, more-accurate replacement.

  Not just for explicit symbol questions. The biggest win is open-ended orientation: exploring an
  unfamiliar .NET solution, scoping a change, or tracing how a type/DTO/field/feature flows for a
  ticket, bugfix or refactor — exactly when you'd instinctively `grep`/`find` to get your bearings.
  Delegating exploration to a subagent? Tell it to use this skill too.

  Triggers: "explore/understand this .NET code", "find references/usages", "who calls X", "go to
  definition", "what implements X", "where is X defined", "find the class/method", "how does this
  flow / where is it set", "what does this class look like / its methods and signatures / outline
  it", "show me method X's body / the source of X", "what's this symbol / its signature / doc",
  "rename symbol", "is this dead code", "find unused", "compiler errors" — or any time a
  .sln/.csproj/.cs/.vb/.fs is in scope.
version: 0.11.1
---

# dotnet-aicraft

Semantic .NET analysis via Roslyn — compiler-grade answers about your code, on demand.

**Costs you nothing to run.** A background daemon auto-starts on first use and idles out after
60 min — you never manage it. The solution load happens in that process, not your context, so it
spends **zero tokens**; you pay only a brief wait on the first query (~50ms each after). A large
solution is therefore a reason to use it, not a cost to avoid.

## Question → command

| Question | Command |
|---|---|
| Where is X used? / what calls it? | `refs` / `callers --symbol "FQN"` |
| What breaks if I change X? blast radius before an edit | `refs` + `callers` (+ `impls`) |
| Every consumer of this type / DTO / field / contract | `refs --symbol "FQN"` |
| Trace how a type/value flows — where set, where read | `refs` / `callers` / `definition` |
| Orient in / scope a change across an unfamiliar solution | `symbols --pattern` → `refs` / `callers` |
| Where is X declared? (from a usage) | `definition --file --line --col` |
| What *is* X? signature, return/param types, modifiers, doc, overloads — without opening the file | `describe --symbol "FQN"` |
| What's a type/file made of? its members & structure (no bodies) — instead of opening the file | `outline --symbol "FQN"` / `outline --file` |
| Read one method's body / a symbol's source — instead of opening the file to find it | `source --symbol "FQN"` |
| What implements or overrides this? (interface / virtual / abstract) | `impls --symbol "FQN"` |
| Is this dead/unused? safe to delete? | `unused` + `refs` |
| Rename a symbol safely | `rename --dry-run` then `rename` |
| Find a symbol by (partial) name | `symbols --pattern "Foo*"` |
| Does it compile? errors / analyzer warnings, before or after a change | `diagnostics --severity error` (or `warning`) |

grep/Glob/Read **miss** what Roslyn finds: renamed locals, interface dispatch, virtual/override
calls, extension methods, generics, partial classes, XML-doc refs.

## More than pinpoint lookups — your orientation tool

Reach for it to *get your bearings* in unfamiliar .NET code — scope a change, trace how a
type/DTO/field flows for a ticket — not just for explicit symbol questions. With compiler certainty
where text search only guesses:

- **Locate by name/fragment** — `symbols --pattern "Foo*"` replaces `find`/`ls` for "where's the
  class/method `Foo`?": every match across the solution with FQN, kind and location.
- **Identity & shape** — replaces opening a file to read code: signature, param/return types,
  modifiers, attributes, XML-doc, overloads (`describe`); a type's members & structure (`outline`);
  one method's verbatim body (`source`). Works even on BCL/NuGet types with no source on disk.
- **Relationships** — who uses / calls / implements / overrides it (`refs` / `callers` / `impls`);
  and from a usage, where it's defined (`definition`).
- **Impact & safety** — full blast radius before a change, build-safe `rename`, dead code
  (`unused`), compiler errors without a full build (`diagnostics`).

**A `file:line` is not a blast radius.** "Defined at `Foo.cs:42`" tells you where a symbol lives,
not who depends on it. The moment the task is "what breaks if I change this" or "every consumer of
this contract", that's `refs`/`callers` — they catch interface dispatch, overrides and extension
methods a name-grep silently misses, so you don't act on an incomplete list.

## Two things to know

- **FQN-first.** Most commands want a fully-qualified name. If you only have a short name,
  run `symbols --pattern "Name*"` and use the `fullName` from the result in follow-ups.
  Overloads resolve to one result group each; pass the parameterized form to disambiguate.
- **Solution auto-discovery.** `-s` is optional from a folder with exactly one
  `.slnx`/`.sln`/`.csproj`. On `SOLUTION_AMBIGUOUS` pass `-s <path>`; on `SOLUTION_NOT_FOUND`
  `cd` into (or pass) a folder with one; on `CONFLICTING_PATH_ARGUMENTS` drop one of `-s`/`-p`.

## Keeping the daemon in sync

Code edits are picked up automatically. Adding or removing a project is not — until you reload,
queries answer against the old project set (a silent wrong answer, not an error):

```bash
dotnet aicraft server reload   # -s <path> only if auto-discovery is ambiguous
```

## More detail

- Exact flags + output schema for one command → `references/commands/<command>.md` — build the
  path directly, no need to list the directory (e.g. `references/commands/rename.md`, `callers.md`,
  `describe.md`, `outline.md`, `source.md`, `unused.md`, `server.md` for daemon management).
- Cross-cutting (global options, output/path conventions, error codes, symbol-name format)
  → `references/commands/_overview.md`
- Workflow patterns + decision trees → `references/patterns.md`
- `dotnet aicraft <command> --help` — authoritative, current flags
