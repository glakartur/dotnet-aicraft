# dotnet-aicraft — Full Command Reference

---

## Global Options

All commands accept:

| Option | Short | Required | Description |
|---|---|---|---|
| `--solution` | `-s` | No | Path to `.sln`/`.slnx` (also accepts `.csproj`/`.vbproj`/`.fsproj`). Optional — auto-discovered from the current directory when it holds exactly one supported file (tier priority `.slnx` → `.sln` → `.csproj`). |
| `--project` | `-p` | No | Path to `.csproj`/`.vbproj`/`.fsproj` (also accepts `.sln`/`.slnx`). Optional — auto-discovered the same way. Passing both `-s` and `-p` with different paths errors with `CONFLICTING_PATH_ARGUMENTS`. |
| `--format` | — | No | Output format: `text` (default, compiler/ripgrep-style — LLM-optimized) or `json` (pretty-printed, stable schema for scripting) |
| `--idle-timeout` | — | No | Session-scoped daemon idle timeout: `off` or duration (`5m`, `30m`, `1h`). Default: `60m` |
| `--debug` | — | No | Verbose debug logging to stderr. Equivalent to `DOTNET_AICRAFT_DEBUG=1`. Debug output is flushed before the stdout result. |

### Path conventions in output

File paths in command results are emitted **relative to the solution
directory**, with forward-slash separators on every platform. The absolute
solution root is surfaced once per response:

- `--format text` — a `SolutionRoot: <abs path>` header line near the top.
- `--format json` — a top-level `solutionRoot` field on the envelope.

The single-symbol/container inspection verbs — `describe`, `outline`, `source` — round out
`definition`: `describe` answers *what is this*, `outline` *what's inside this container*, `source`
*show me just this symbol's text*. See [`describe.md`](describe.md), [`outline.md`](outline.md),
[`source.md`](source.md).

For commands that return lists (`refs`, `impls`, `symbols`,
`diagnostics`, `unused`), the JSON envelope is:

```json
{ "solutionRoot": "/abs/path", "items": [ ... ] }
```

Out-of-tree paths (different volume, generator output outside the solution
tree) fall back to absolute form with forward-slash normalization.

`rename` keeps its existing summary shape (no `SolutionRoot:` text header line);
relative paths still apply inside its `changes[]`.

---

---

## Error Output

When a command fails, the JSON envelope carries an `error` object:

```json
{ "error": { "code": "SOLUTION_UNAVAILABLE",
             "message": "Solution is currently unavailable.",
             "details": { "hint": "Run 'server reload' or fix project files and retry." } } }
```

In `--format text`, errors render as:

```
error SOLUTION_UNAVAILABLE: Solution is currently unavailable.
hint: Run 'server reload' or fix the solution/project files and retry.
```

Always check for the `error` field when parsing JSON output programmatically.

---

---

## Symbol Name Format

Fully-qualified names follow C# namespace conventions:

| Kind | Example |
|---|---|
| Namespace | `MyApp.Services` |
| Type | `MyApp.Services.OrderService` |
| Method | `MyApp.Services.OrderService.ProcessOrder` |
| Property | `MyApp.Services.OrderService.IsActive` |
| Field | `MyApp.Services.OrderService._cache` |
| Interface | `MyApp.Interfaces.IOrderProcessor` |

When unsure of the fully-qualified name, use `symbols --pattern` to discover it.
