---
name: dotnet-aicraft
description: >
  Roslyn-powered semantic code intelligence for .NET solutions (.sln / .csproj) via the `dotnet aicraft`
  CLI — compiler-precise answers about symbols, references, call graphs, implementations, diagnostics,
  renames, and dead code. A background daemon keeps the solution loaded, so queries return in ~50ms.

  LOAD THIS SKILL EAGERLY — do not skip it. If there is even a hint that the work touches a .NET / C# /
  F# / VB.NET codebase (a .sln, .csproj, .cs, .vb, .fs file is visible, mentioned, or in scope), load this
  skill BEFORE reaching for grep, Glob, ripgrep, Read, or any text search. Text search misses interface
  dispatch, virtual/override calls, extension methods, generics, partial classes, renamed locals, and XML
  doc references — `dotnet aicraft` finds them all.

  Use this skill for ANY of the operations it supports:
    • refs            — find ALL references / usages / call sites of a symbol
    • callers         — incoming/outgoing call graph, configurable depth
    • definition      — jump to declaration (from file+line+col or from FQN)
    • impls           — find all implementations of an interface or overrides of a virtual/abstract member
    • rename          — safe semantic rename across the whole solution (always with --dry-run first)
    • symbols         — pattern search for classes/interfaces/methods/properties/fields/etc. + FQN discovery
    • unused          — detect dead code / unused methods / unused classes / unused members
    • diagnostics     — Roslyn compiler diagnostics (errors, warnings) per solution/project/file

  Load PROACTIVELY when:
    - onboarding to or exploring an unfamiliar .NET codebase
    - about to modify, move, delete, or rename any method, class, interface, struct, enum, property, field, or event
    - planning ANY refactoring in .NET code
    - assessing blast radius / impact before a change
    - checking whether something is safe to delete
    - navigating inheritance, interface hierarchies, or call chains
    - resolving a symbol's declaration from a usage site
    - investigating compile errors or warnings
    - hunting for dead / unreachable / unused code
    - looking up a symbol by partial or fuzzy name

  Trigger on user phrasing such as: "find references", "find all usages", "who calls X", "what calls X",
  "what does X call", "where is X defined", "where is X declared", "go to definition", "find implementations",
  "find implementors", "what implements X", "rename this", "rename symbol", "is this used", "is X used anywhere",
  "can I delete this", "is this dead code", "find unused code", "list unused methods", "search symbols",
  "find a class named", "find a method called", "show me the call graph", "incoming callers", "outgoing calls",
  "compiler errors", "Roslyn diagnostics".

  ALWAYS prefer `dotnet aicraft` over grep / Glob / ripgrep / file-reading for any symbol-level question in
  a .NET project — it is faster, exhaustive, and semantically correct. Falling back to text search is only
  acceptable for non-semantic discovery (free text in comments, config files, markdown).
version: 0.6.0
---

# dotnet-aicraft

Semantic .NET code analysis via Roslyn — compiler-level precision, not text search. A background daemon loads the solution once; subsequent commands respond in ~50ms. Auto-starts on first use, shuts down after 60 min idle.

## Never Use Text Search for Symbol Questions

**Do NOT use grep, Glob, or Read to answer these questions in a .NET project:**

| Question | Wrong approach | Correct command |
|---|---|---|
| Where is symbol X used? | `grep "MethodName"` | `refs --symbol "FQN"` |
| What calls this method? | grep for call patterns | `callers --symbol "FQN"` |
| Is this code dead/unused? | grep for the name | `unused` + `refs` |
| What implements this interface? | grep for `: IFoo` | `impls --symbol "FQN"` |
| Where is X declared? | Read files | `definition --symbol "FQN"` |
| Does renaming X break anything? | manual grep | `rename --dry-run` |

Text search **misses**: renamed variables, interface dispatch, virtual/override calls, extension methods, and XML doc references. Roslyn finds all of them.

## Solution Discovery

If the solution path is unknown:

```bash
find . -name "*.sln" -maxdepth 4 | head -5
# or check the project root directly
ls *.sln 2>/dev/null
```

Pass the found path as `-s <path>` to every command.

## Discovering Fully-Qualified Names

Most commands require a fully-qualified symbol name (FQN). When you only have a short name, discover it first:

```bash
dotnet aicraft symbols -s App.sln --pattern "MethodName*"
dotnet aicraft symbols -s App.sln --pattern "*ClassName*" --kind class
```

Use the `fullName` field from the result in all follow-up commands.

## Output Format

Commands emit a **compiler/ripgrep-style text format on stdout by default** — optimized for LLM reading.

```
SolutionRoot: /abs/path/to/repo

references:
src/Controllers/OrderController.cs:87:9: _orderService.ProcessOrder(dto.ToRequest());
```

Use `--format json` when you need to process results programmatically (count items, filter, iterate fields). For reading and understanding results, the default text format is sufficient and more concise.

File paths in results are **relative to the solution directory** with forward-slash separators. The absolute root is surfaced once per response:
- In `--format text`: a `SolutionRoot: <abs path>` header line.
- In `--format json`: a top-level `solutionRoot` field on the envelope.

For list-shaped results (`refs`, `impls`, `symbols`, `diagnostics`, `unused`) the JSON envelope is `{ "solutionRoot": "...", "items": [...] }` — the list lives under `items`, not as a top-level array.

## When to Use Proactively

| Situation | Command |
|---|---|
| About to modify a method or property | `refs` — know all call sites first |
| About to delete a class or member | `refs` + `callers` + `unused` — confirm impact |
| About to rename anything | `rename --dry-run` then `rename` |
| Need declaration from usage location | `definition --file --line --col` |
| Implementing a feature touching an interface | `impls` — discover all implementors |
| Exploring an unfamiliar codebase | `symbols --pattern "*"` + `impls` on key interfaces |
| Need compiler signal before edits | `diagnostics --severity error` |
| Need call hierarchy | `callers --direction incoming\|outgoing\|both --depth N` |
| Partial symbol name known | `symbols --pattern "Partial*"` |
| Projects added/removed | `server reload` |

## Shared Options

```bash
--solution / -s   # Path to .sln or .csproj (required)
--format          # text (default, LLM-optimized) | json (stable schema for scripting)
--idle-timeout    # "off" or duration like "5m", "1h" (default 60m, session-scoped)
--debug           # Verbose debug logging to stderr (also: DOTNET_AICRAFT_DEBUG=1)
```

## Identifying Symbols

```bash
# By file location (preferred when reading source)
--file path/to/File.cs --line 42 --col 18

# By fully-qualified name (preferred when name is known)
--symbol "MyApp.Services.OrderService.ProcessOrder"
```

## Commands

### refs — Find All References

```bash
dotnet aicraft refs -s App.sln --file src/Services/OrderService.cs --line 42 --col 18
dotnet aicraft refs -s App.sln --symbol "MyApp.Services.OrderService.ProcessOrder"
```

Output (`--format json`): `{ solutionRoot, items: [{ file, line, col, context }] }`
Output (`--format text`, default):
```
SolutionRoot: /abs/path/to/repo

references:
src/Controllers/OrderController.cs:87:9: _orderService.ProcessOrder(...);
...
```

### rename — Safe Rename

Always dry-run first:

```bash
dotnet aicraft rename -s App.sln --symbol "MyApp.Services.OrderService.ProcessOrder" --to "HandleOrder" --dry-run
dotnet aicraft rename -s App.sln --symbol "MyApp.Services.OrderService.ProcessOrder" --to "HandleOrder"
```

Output: `{ symbol, newName, applied, dryRun, changes[] }`

### definition — Find Declaration

```bash
dotnet aicraft definition -s App.sln --file Services/OrderService.cs --line 42 --col 18
dotnet aicraft definition -s App.sln --symbol "MyApp.Services.OrderService.ProcessOrder"
```

Output: `DefinitionResult` — if metadata-only, `file/line/col` will be null. Use `fullName` for follow-up commands.

### impls — Find Implementations

```bash
dotnet aicraft impls -s App.sln --symbol "MyApp.Interfaces.IOrderProcessor"
```

Output: JSON array of implementing types with file locations.

### callers — Call Graph

```bash
# Default: incoming callers, depth=1
dotnet aicraft callers -s App.sln --symbol "MyApp.Services.OrderService.ProcessOrder"

# Full graph
dotnet aicraft callers -s App.sln --symbol "MyApp.Services.OrderService.ProcessOrder" --direction both --depth 2
```

Output: `CallGraphResult { rootId, direction, depth, nodes[], edges[] }`

### diagnostics — Roslyn Diagnostics

```bash
dotnet aicraft diagnostics -s App.sln --severity error
dotnet aicraft diagnostics -s App.sln --project MyApp.Core
dotnet aicraft diagnostics -s App.sln --file src/Services/OrderService.cs
```

Output: sorted `[{ project, id, severity, message, file, line, col }]`

### unused — Dead-Code Candidates

```bash
dotnet aicraft unused -s App.sln --kind method
dotnet aicraft unused -s App.sln --project MyApp.Core --public-only
dotnet aicraft unused -s App.sln --kind class --include-generated
```

Output: `UnusedScanSummary { scanned, items[{ symbol, kind, reason, confidence }] }`

### symbols — Pattern Search

```bash
dotnet aicraft symbols -s App.sln --pattern "Process*"
dotnet aicraft symbols -s App.sln --pattern "Process*" --kind method
dotnet aicraft symbols -s App.sln --pattern "*" --kind class --limit 100 --offset 200
```

Kinds: `class|interface|struct|enum|delegate|method|constructor|property|field|event|type|member|namespace|all`
Output: `SymbolsResultPage { items[], hasMore }`

### server — Daemon Management

```bash
dotnet aicraft server status -s App.sln         # daemon status + solution stats
dotnet aicraft server reload -s App.sln         # reload after project structure changes
dotnet aicraft server stop   -s App.sln         # stop the daemon
dotnet aicraft server start  -s App.sln --idle-timeout 30m   # ensure daemon is running + set / extend session idle timeout (returns promptly)
dotnet aicraft server start  -s App.sln --idle-timeout off   # disable idle auto-shutdown (returns promptly)
```

## Agent Workflows

### Refactoring a symbol
1. Find FQN via `symbols` or source reading.
2. `rename --dry-run` → inspect `changes[]`.
3. `rename` to apply.

### Investigating before deletion
1. `refs` → all usages.
2. `callers --direction incoming --depth 1` → immediate callers.
3. `unused` on same kind/project → dead-code confidence.

### Exploring an interface
1. `impls` → all implementing types.
2. `definition` on each implementor for follow-up.

### Declaration from cursor
1. `definition --file --line --col`.
2. Use returned `fullName` for `refs`, `callers`, `rename`.

### Diagnostics triage before refactor
1. `diagnostics --severity error` → fix blockers first.
2. Narrow by `--project` or `--file` as needed.

## Reference Files

- `references/commands.md` — expanded examples and output samples
- `references/patterns.md` — workflow patterns and decision trees

`dotnet aicraft <command> --help` is the source of truth for current flags.
