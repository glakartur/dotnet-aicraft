## `dotnet aicraft diagnostics`

List Roslyn diagnostics across the solution with filters.

### Options

_Plus the global options — `-s`/`--solution`, `-p`/`--project`, `--format`, `--idle-timeout`, `--debug` — documented once in [`_overview.md`](_overview.md)._

| Option | Required | Description |
|---|---|---|
| `--severity` | No | Exact-match filter (not a threshold — each value returns only that level): `all` (default), `error`, `warning`, `info`, `hidden`. Use `error` for compile blockers, `warning` for analyzer/style ("bad practices"); `all` also includes `hidden` (IDE suggestions — noisy). To see both errors and warnings, run two passes. |
| `--project-name` | No | Restrict results to a single project by name |
| `--file` | No | Restrict to a single file |

### Example

```bash
dotnet aicraft diagnostics --solution App.sln --severity warning
dotnet aicraft diagnostics --solution App.sln --project-name MyApp.Core --file src/Services/OrderService.cs
```

### Output (`--format text`, MSBuild-style)

```
SolutionRoot: /abs/path

diagnostics:
error src/Bar.cs:88:1 [CS0103]: The name 'foo' does not exist in the current context
warning src/Foo.cs:42:5 [CS0168]: The variable 'x' is declared but never used
```

### Output Schema (`--format json`)

```json
{
  "solutionRoot": "/abs/path",
  "items": [
    { "project": "MyApp.Core", "id": "CS0168", "severity": "warning",
      "message": "...", "file": "src/Foo.cs", "line": 42, "col": 5 }
  ]
}
```

---
