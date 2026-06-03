## `dotnet aicraft definition`

Resolve the declaration of a symbol by source location or fully-qualified name.

### Options

_Plus the global options — `-s`/`--solution`, `-p`/`--project`, `--format`, `--idle-timeout`, `--debug` — documented once in [`_overview.md`](_overview.md)._

| Option | Required | Description |
|---|---|---|
| `--file` | If not using `--symbol` | Source file path |
| `--line` | With `--file` | 1-based line number |
| `--col` | With `--file` | 1-based column number |
| `--symbol` | If not using `--file` | Fully-qualified symbol name |

### Output Schema (`--format json`)

Grouped per matched symbol (one group per overload / constructor). Each `result` is the
definition record; `file/line/col` may be null for metadata-only symbols.

```json
{
  "solutionRoot": "/abs/path/to/repo",
  "items": [
    {
      "symbol": "MyApp.Services.OrderService.ProcessOrder(MyApp.Contracts.OrderRequest)",
      "kind": "method",
      "result": {
        "fullName": "MyApp.Services.OrderService.ProcessOrder(MyApp.Contracts.OrderRequest)",
        "kind": "method",
        "file": "src/Services/OrderService.cs",
        "line": 42,
        "col": 18,
        "containingType": "MyApp.Services.OrderService",
        "containingNamespace": "MyApp.Services"
      }
    }
  ]
}
```

Use `result.fullName` for follow-up commands (`refs`, `callers`, `rename`).

---
