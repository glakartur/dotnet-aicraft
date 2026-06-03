## `dotnet aicraft callers`

Find call sites that invoke a method (call hierarchy). Each matched symbol gets a
`CallGraphResult` graph (regardless of direction or depth), wrapped in a result group.

### Options

_Plus the global options — `-s`/`--solution`, `-p`/`--project`, `--format`, `--idle-timeout`, `--debug` — documented once in [`_overview.md`](_overview.md)._

| Option | Required | Description |
|---|---|---|
| `--file` | If not using `--symbol` | Source file path |
| `--line` | With `--file` | 1-based line number |
| `--col` | With `--file` | 1-based column number |
| `--symbol` | If not using `--file` | Fully-qualified method name |
| `--direction` | No | `incoming` (default), `outgoing`, or `both` |
| `--depth` | No | Traversal depth (default 1) |

### Examples

```bash
# Incoming callers, depth=1 (default) — by name
dotnet aicraft callers --solution App.sln \
  --symbol "MyApp.Services.OrderService.ProcessOrder"

# Incoming callers, depth=1 — by file location
dotnet aicraft callers --solution App.sln \
  --file src/Services/OrderService.cs --line 42 --col 18

# Full graph — both directions, depth 2
dotnet aicraft callers --solution App.sln \
  --symbol "MyApp.Services.OrderService.ProcessOrder" \
  --direction both --depth 2
```

### Output Schema (`--format json`)

Grouped per matched symbol; each `result` is a `CallGraphResult`:

```json
{
  "solutionRoot": "/abs/path",
  "items": [
    {
      "symbol": "MyApp.Services.OrderService.ProcessOrder(MyApp.Contracts.OrderRequest)",
      "kind": "method",
      "result": {
        "rootId": "MyApp.Services.OrderService.ProcessOrder(MyApp.Contracts.OrderRequest)",
        "direction": "incoming",
        "depth": 1,
        "nodes": [
          { "id": "MyApp.Services.OrderService.ProcessOrder(...)", "fullName": "...", "kind": "method",
            "file": "src/Services/OrderService.cs", "line": 42, "col": 18,
            "containingType": "MyApp.Services.OrderService", "containingNamespace": "MyApp.Services" }
        ],
        "edges": [
          { "from": "MyApp.Controllers.OrderController.Post(...)", "to": "MyApp.Services.OrderService.ProcessOrder(...)",
            "relation": "incoming", "isDirect": true }
        ]
      }
    }
  ]
}
```

---
