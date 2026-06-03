## `dotnet aicraft refs`

Find all references to a symbol in the solution.

### Options

_Plus the global options — `-s`/`--solution`, `-p`/`--project`, `--format`, `--idle-timeout`, `--debug` — documented once in [`_overview.md`](_overview.md)._

| Option | Required | Description |
|---|---|---|
| `--file` | If not using `--symbol` | Source file path |
| `--line` | With `--file` | 1-based line number |
| `--col` | With `--file` | 1-based column number |
| `--symbol` | If not using `--file` | Fully-qualified symbol name |

### Examples

```bash
dotnet aicraft refs --solution App.sln \
  --file src/Services/OrderService.cs --line 42 --col 18

dotnet aicraft refs --solution App.sln \
  --symbol "MyApp.Services.OrderService.ProcessOrder"
```

### Output Schema (`--format json`)

Results are grouped per matched symbol: a parameterless fully-qualified name can match
several overloads (and a constructor name matches each constructor), so `refs` returns one
group per match, each with its own `result` array.

```json
{
  "solutionRoot": "/abs/path/to/repo",
  "items": [
    {
      "symbol": "MyApp.Services.OrderService.ProcessOrder(MyApp.Contracts.OrderRequest)",
      "kind": "method",
      "result": [
        {
          "file": "src/Controllers/OrderController.cs",
          "line": 87,
          "col": 9,
          "context": "_orderService.ProcessOrder(dto.ToRequest());"
        }
      ]
    }
  ]
}
```

### Output (`--format text`, default)

```
SolutionRoot: /abs/path/to/repo

match: method MyApp.Services.OrderService.ProcessOrder(MyApp.Contracts.OrderRequest)
references:
src/Controllers/OrderController.cs:87:9: _orderService.ProcessOrder(dto.ToRequest());
```

`refs`, `impls`, `callers`, and `definition` group their output per matched symbol:
each group is a `{ symbol, kind, result }` item under `items` (JSON), and in text format
each is introduced by a `match: <kind> <symbol>` header before its `<label>:` section.
The other list commands (`symbols`, `unused`, `diagnostics`) keep a flat `items` array.

---
