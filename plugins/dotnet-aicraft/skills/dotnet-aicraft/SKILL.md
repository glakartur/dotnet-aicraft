---
name: dotnet-aicraft
description: >
  Understand and safely change .NET/C#/F#/VB.NET code with compiler-grade certainty: find every
  real reference, caller, implementation and override; jump to definitions; inspect a symbol's
  signature/types/doc, outline what a type or file declares, or read just one symbol's source;
  rename symbols without breaking the build; surface dead code and compiler errors. The payoff is
  correct answers and safe edits where text search quietly gives wrong ones — and far less time
  spent reading whole files (or guessing at BCL/NuGet types that have no source on disk).

  STOP before you locate, search, trace, or read .NET/C#/F#/VB.NET code by any text- or
  file-based means — whether a Bash command (`grep -r`, `find -name "*.cs"`, `cat`/`head` a
  `.cs`/`.vb`/`.fs` file, `ls`) or a dedicated tool (Search, Grep, Glob, Read). Use this instead;
  it is their drop-in replacement and it is more accurate, because text search misses interface
  dispatch, overrides, extension methods, generics and partial classes.

  This is NOT only for explicit symbol questions. The most common moment to reach for it is
  open-ended orientation: exploring or understanding an unfamiliar .NET codebase, scoping a
  change, or tracing how a type/DTO/table/field/feature flows through the code as part of any
  task (a Jira ticket, a bugfix, a refactor). The instinct to `grep -r`/`find` your way around
  to get your bearings is exactly when this answers the question correctly and faster. When you
  delegate exploration to a subagent, tell it to use this skill too.

  Triggers: "find references/usages", "who calls X", "go to definition", "what implements X",
  "where is X defined", "find/locate the class/method/file", "how does this flow / where is it
  set", "what is this symbol / what's its signature / show me its doc", "what members does this
  type/file have / outline this type", "show me the source of X / read just this method", "rename
  symbol", "is this dead code", "find unused", "compiler errors" — and any time a
  .sln/.csproj/.cs/.vb/.fs is in scope or mentioned.
version: 0.11.0
---

# dotnet-aicraft

Semantic .NET analysis via Roslyn — compiler-grade answers about your code, on demand.

**It costs you nothing and isn't yours to manage.** A background daemon auto-starts on first use
and idles out after 60 min — you never spin it up or tend it. The solution load (even dozens of
projects) happens inside that process, not your context, so it spends **zero tokens**; your only
outlay is a brief wait on the first query (~50ms each after). The payoff is precise, trustworthy
results — so a large solution is a reason to use it, not a cost to avoid.

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

grep/Glob/Read **miss**: renamed locals, interface dispatch, virtual/override calls,
extension methods, generics, partial classes, XML-doc refs. Roslyn finds all of them.

## What you can get from it

This is also your tool for *orienting* in unfamiliar .NET code — scoping a change, tracing how a
type/DTO/field flows, getting your bearings on a ticket — not just for pinpoint lookups. The
questions it answers with compiler certainty, where text search only guesses:

- **Locate by name or fragment** — the semantic replacement for `find`/`ls` when you're hunting
  "where's the class/method called `Foo`?": `symbols --pattern "Foo*"` returns every match across
  the whole solution with its fully-qualified name, kind and location. No path-guessing, no
  walking namespaces by hand.
- **Identity & shape** — your replacement for opening a file to *read code*: a symbol's signature,
  parameter/return types, modifiers, attributes, XML-doc and overloads (`describe`); a type's
  members & structure (`outline`); and one method's verbatim body or a type's source — just that
  block, with its span — via `source`, instead of `Read`-ing the whole file to find it. When you
  catch yourself about to open a file to see a method body or learn a class's shape, reach here
  first. These even work on BCL/NuGet types with no source on disk, so you stop guessing at
  framework signatures.
- **Relationships** — who uses, calls, implements or overrides it (`refs` / `callers` / `impls`);
  and from any usage, where it's defined (`definition`).
- **Impact & safety** — the full blast radius before a change, a build-safe `rename`, dead-code
  candidates (`unused`), and compiler errors/warnings without a full build (`diagnostics`).

The mid-task trap worth naming: **a `file:line` for a definition is not its blast radius.** A
plan that says "defined at `Foo.cs:42`" tells you where the symbol lives, not who depends on it —
the moment the task becomes "what breaks if I change this" or "inventory every consumer of this
contract", that's `refs`/`callers`. They see interface dispatch, overrides and extension methods
that a grep of the name silently misses, handing you an incomplete list you'd then trust.

grep still wins for genuine *text* — string literals, comments, log messages, headers, config
values, `using` lines, markdown — so use it there without hesitation, and reach here the moment
the question is about a symbol.

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
