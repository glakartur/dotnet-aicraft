---
name: dotnet-aicraft
description: >
  Understand and safely change .NET/C#/F#/VB.NET code with compiler-grade certainty: find every
  real reference, caller, implementation and override; jump to definitions; rename symbols
  without breaking the build; surface dead code and compiler errors. The payoff is correct
  answers and safe edits on symbol-level questions where text search quietly gives wrong ones —
  with far less time spent hunting through files.

  Use this PROACTIVELY whenever .NET/C#/F#/VB.NET code is in scope (any .sln/.csproj/.cs/.vb/.fs
  visible or mentioned). It is a drop-in replacement for the Search tool, grep/Glob/ripgrep and
  Read for any symbol-level question — reach for it FIRST, since text search misses interface
  dispatch, overrides, extension methods, generics and partial classes. Triggers: "find
  references/usages", "who calls X", "go to definition", "what implements X", "rename symbol",
  "is this dead code", "find unused", "compiler errors".
version: 0.8.1
---

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
| Does it compile? errors / analyzer warnings, before or after a change | `diagnostics --severity error` (or `warning`) |

grep/Glob/Read **miss**: renamed locals, interface dispatch, virtual/override calls,
extension methods, generics, partial classes, XML-doc refs. Roslyn finds all of them.

## Two things to know

- **FQN-first.** Most commands want a fully-qualified name. If you only have a short name,
  run `symbols --pattern "Name*"` and use the `fullName` from the result in follow-ups.
  Overloads resolve to one result group each; pass the parameterized form to disambiguate.
- **Solution auto-discovery.** `-s` is optional from a folder with exactly one
  `.slnx`/`.sln`/`.csproj`. On `SOLUTION_AMBIGUOUS` pass `-s <path>`; on `SOLUTION_NOT_FOUND`
  `cd` into (or pass) a folder with one; on `CONFLICTING_PATH_ARGUMENTS` drop one of `-s`/`-p`.

## More detail

- Exact flags + output schema for one command → `references/commands/<command>.md` — build the
  path directly, no need to list the directory (e.g. `references/commands/rename.md`, `callers.md`,
  `unused.md`, `server.md` for daemon management — `reload` after projects are added/removed).
- Cross-cutting (global options, output/path conventions, error codes, symbol-name format)
  → `references/commands/_overview.md`
- Workflow patterns + decision trees → `references/patterns.md`
- `dotnet aicraft <command> --help` — authoritative, current flags
