# dotnet-aicraft — Agent Workflow Patterns

## Decision Tree: Which Command to Use

```
Need to find something?
├── Know the symbol name? → refs or callers with --symbol
├── Reading a file and have line/col? → refs or callers with --file --line --col
├── Partial name only? → symbols --pattern "Partial*"
├── Where is it declared? → definition --symbol (or --file --line --col)
├── Looking for interface implementors? → impls --symbol "Namespace.IInterface"
└── Need a type's inheritance lineage (base chain or derived types)? → hierarchy --symbol "FQN" --direction up|down

Need to understand something (instead of reading the whole file)?
├── What IS this symbol? signature, types, modifiers, attrs, doc, overloads → describe
├── What's declared inside this type/file? (members, no bodies) → outline
└── Show me just this symbol's source text → source

Need to change something?
└── Rename → rename --dry-run first, then rename to apply
```

The inspection trio (`describe` / `outline` / `source`) answers from the in-memory daemon and works on
metadata symbols (BCL/NuGet) that have no file on disk — reach for them instead of `Read`-ing a whole
file to learn a signature, a type's shape, or one member's body.

`impls` vs `hierarchy` — both are about "what relates to this type", but answer different questions.
`impls` is *interface/abstract member → concrete implementations*. `hierarchy` is *type → type
inheritance lineage*: a class's base-type chain (`up`) or its transitive subclasses (`down`), or an
interface's extended/derived interfaces. If the question is "what subclasses this base class" or
"what's this type's ancestry", that's `hierarchy`, not `impls`.

---

## Pattern 1: Understand Impact Before Refactoring

Before removing or modifying a symbol, assess how many places depend on it.

```bash
# Step 1: Find all usages
dotnet aicraft refs --solution App.sln \
  --symbol "MyApp.Services.OrderService.ProcessOrder"

# Step 2: Find all callers
dotnet aicraft callers --solution App.sln \
  --symbol "MyApp.Services.OrderService.ProcessOrder"

# Step 3: Find implementations if it's an interface member
dotnet aicraft impls --solution App.sln \
  --symbol "MyApp.Interfaces.IOrderProcessor.ProcessOrder"

# Step 4: If it's a base class/interface, find everything that inherits from it —
# a behaviour change ripples to every transitive subclass/derived interface.
dotnet aicraft hierarchy --solution App.sln \
  --symbol "MyApp.Domain.EntityBase" --direction down
```

Decide based on the full evidence set — not grep output.

---

## Pattern 2: Safe Symbol Rename

```bash
# Step 1: Discover the exact fully-qualified name
dotnet aicraft symbols --solution App.sln --pattern "ProcessOrder" --kind member

# Step 2: Dry-run the rename
dotnet aicraft rename --solution App.sln \
  --symbol "MyApp.Services.OrderService.ProcessOrder" \
  --to "HandleOrder" --dry-run

# Step 3: Inspect changes[] in the output
# Verify the file list and context snippets look correct

# Step 4: Apply
dotnet aicraft rename --solution App.sln \
  --symbol "MyApp.Services.OrderService.ProcessOrder" \
  --to "HandleOrder"
```

Never skip the dry-run. It costs nothing and prevents surprises.

---

## Pattern 3: Explore an Unknown Codebase

When starting work on an unfamiliar solution:

```bash
# Find all types in a namespace
dotnet aicraft symbols --solution App.sln --pattern "MyApp.Services.*" --kind type

# Find all methods matching a domain concept
dotnet aicraft symbols --solution App.sln --pattern "*Order*" --kind member

# Discover who implements a central interface
dotnet aicraft impls --solution App.sln \
  --symbol "MyApp.Core.IRepository"

# Trace how a key method is used
dotnet aicraft callers --solution App.sln \
  --symbol "MyApp.Core.IRepository.GetById"

# See a type's shape without opening its file (members + signatures + line numbers)
dotnet aicraft outline --solution App.sln --symbol "MyApp.Services.OrderService"

# Or outline every top-level type a file declares
dotnet aicraft outline --solution App.sln --file src/Services/OrderService.cs
```

---

## Pattern 3b: Understand a Symbol Without Reading Its File

When you need to know *what a symbol is* or *what's in it* — and reading the whole file (or, for a
BCL/NuGet type, guessing from memory) would be wasteful:

```bash
# The semantic card: signature, return/param types, modifiers, attributes, cleaned XML-doc,
# and sibling overloads. Works on metadata symbols too (null file/line, names the assembly).
dotnet aicraft describe --solution App.sln --symbol "MyApp.Services.OrderService.Process"

# The members a type declares (declared-only; add --include-inherited for the base chain,
# --public-only for the consumable/extensible surface).
dotnet aicraft outline --solution App.sln --symbol "MyApp.Services.OrderService" --public-only

# Just this symbol's verbatim declaration text, with its file + line span. partial types /
# overloads return one block per part; metadata symbols degrade to a non-error "no source" note.
dotnet aicraft source --solution App.sln --symbol "MyApp.Services.OrderService.Process"
```

`describe`/`source` resolve overloads to one result group each. On an ambiguous overload, pass the
parameterized form (`...Process(MyApp.OrderDto)`). `outline` takes `--symbol <type>` **or** a bare
`--file` and rejects `--line`/`--col`.

---

## Pattern 4: Navigate from File + Line to Symbol

When reading source code and the cursor is at a specific location:

```bash
# At line 42, col 18 in OrderService.cs — what is this?
dotnet aicraft refs --solution App.sln \
  --file src/Services/OrderService.cs --line 42 --col 18

# Follow the call hierarchy upward
dotnet aicraft callers --solution App.sln \
  --file src/Services/OrderService.cs --line 42 --col 18

# Or get the full semantic card for whatever symbol is under the cursor — including
# BCL/NuGet symbols, which resolve from metadata at that position
dotnet aicraft describe --solution App.sln \
  --file src/Services/OrderService.cs --line 42 --col 18
```

Column numbers in editors are usually 1-based. The `context` field in results confirms which symbol was resolved.

---

## Pattern 5: Daemon Lifecycle Management

### Normal workflow (automatic)

The daemon starts automatically on first use. No explicit management needed unless:
- The solution structure changed (projects added/removed)
- The daemon is using excessive memory
- Debugging connectivity issues

### After adding/removing projects

```bash
dotnet aicraft server reload --solution App.sln
```

### Diagnosing slow responses

```bash
dotnet aicraft server status --solution App.sln
```

Check `running: true` and `projects` / `documents` counts. If not running, the next command will restart it.

### Long-running agent session

Prevent idle shutdown during an extended session. `server start` returns
immediately after applying the timeout — no foreground process to manage:

```bash
dotnet aicraft server start --solution App.sln --idle-timeout off
```

Or extend the timeout:

```bash
dotnet aicraft server start --solution App.sln --idle-timeout 4h
```

---

## Pattern 6: Parallel Multi-Solution Analysis

Each solution runs its own daemon. Run commands concurrently across solutions:

```bash
dotnet aicraft refs --solution Backend.sln --symbol "MyApp.Shared.Events.OrderCreated" &
dotnet aicraft refs --solution Frontend.sln --symbol "MyApp.Shared.Events.OrderCreated" &
wait
```

---

## Pattern 7: Parsing JSON Output Programmatically

All commands output clean JSON to stdout. Daemon logs go only to stderr.

```bash
# Capture JSON only
refs_output=$(dotnet aicraft refs --solution App.sln --symbol "MyApp.Foo" 2>/dev/null)

# Check for errors
if echo "$refs_output" | jq -e '.error' > /dev/null 2>&1; then
  echo "Error: $(echo $refs_output | jq -r '.error')"
fi
```

In scripts or agents processing the output, always check for the `error` field before iterating `results`.

---

## Pattern 8: Diagnostics as a Fast Pre-Flight / Post-Edit Check

`dotnet aicraft diagnostics --severity error` answers from the already-loaded
daemon in ~50ms — far cheaper than a full `dotnet build` — so it is worth running
as a quick gate around edits, not just when chasing a reported error.

```bash
# Before a refactor: capture the baseline, so you can tell errors your change
# introduces apart from ones that were already there.
dotnet aicraft diagnostics --severity error

# After an edit or rename: confirm you didn't add new errors.
dotnet aicraft diagnostics --severity error

# Before running the suite: compile errors fail the test build anyway — catch
# them here instead of waiting for `dotnet test`. Narrow to the area you touched.
dotnet aicraft diagnostics --severity error --project-name MyApp.Core
```

Narrow with `--project-name` or `--file` to focus on what you changed.
`--severity` is an **exact-match** filter, not a threshold: `error` returns only
errors, `warning` only warnings. So a thorough post-edit pass is two calls —
`--severity error` for compile blockers, then `--severity warning` for
analyzer/style ("bad practices"). Avoid bare `--severity all` as a default: it
also returns `hidden` IDE suggestions and is usually noise. This is a Roslyn
semantic check, not a style linter — it sees what the compiler and the
configured analyzers see.

---

## Common Mistakes to Avoid

| Mistake | Fix |
|---|---|
| Using text search (grep) for refactoring | Use `rename` — it handles all call sites atomically |
| Applying rename without dry-run | Always run `--dry-run` first |
| Passing only the method name to `--symbol` | Use the fully-qualified name: `Namespace.Class.Method` |
| Assuming the daemon is always running | Check with `server status` if responses seem slow |
| Using 0-based line/col numbers | `dotnet aicraft` uses **1-based** line and column numbers |
| Expecting stderr in JSON output | Daemon startup messages go to stderr; JSON is always clean on stdout |

---

## Output Field Reference Quick Lookup

### `refs` and `callers`
- `file` — path relative to solution root (forward-slash separators on all platforms)
- `line` — 1-based line number of the reference
- `col` — 1-based column number
- `context` — source line text at that location

### `rename`
- `symbol` — original fully-qualified name
- `newName` — the new identifier (not fully-qualified)
- `applied` — `true` if changes were written to disk
- `dryRun` — `true` if `--dry-run` was passed
- `changes[].oldText` / `changes[].newText` — text before and after rename

### `impls`
- `symbol` — fully-qualified name of the implementing type/member
- `file`, `line`, `col`, `context` — location of the implementation declaration

### `symbols`
- `name` — short identifier name
- `fullName` — fully-qualified name to use in subsequent commands
- `kind` — `method`, `class`, `interface`, `property`, `field`, `namespace`, etc.
- `file`, `line`, `col` — declaration location (file is relative to solution root)

### `describe`
- grouped per matched symbol (`{ symbol, kind, result }`); `result` is the card
- `signature` — accessibility + modifiers + return type + name + params (type header for a type)
- `returnType`, `parameters[]` (`{ name, type, defaultValue? }`), `modifiers[]`, `attributes[]` (short names)
- `constantValue` — for enum members / `const` fields
- `documentation` — cleaned XML-doc; `siblings[]` — other overload signatures (excludes the target)
- `file`/`line`/`col` are **null** for metadata symbols, which instead carry `assembly`

### `outline`
- grouped per container; `result` is `{ container, kind, publicOnly, includeInherited, declared[], inherited[] }`
- `declared[]` — `{ file, line, col, declaringType, signature, tag? }` (flat located lines, source order)
- `inherited[]` — `{ declaringType, assembly?, members[] }` (only with `--include-inherited`; `assembly` set for metadata bases)
- a member's `tag` is `"hidden by new"` when it is shadowed by a declared `new` member

### `source`
- grouped per matched symbol; `result` is `{ fullName, kind, hasSource, blocks[], assembly?, note? }`
- `blocks[]` — `{ file, startLine, endLine, text }`, one per `partial`/overload part; the span bounds `text`
- `hasSource: false` + `note` (+ `assembly`) for metadata-only or compiler-generated symbols (non-error)
