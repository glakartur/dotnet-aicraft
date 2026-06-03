## `dotnet aicraft impls`

Find all implementations of an interface or abstract member.

### Options

_Plus the global options — `-s`/`--solution`, `-p`/`--project`, `--format`, `--idle-timeout`, `--debug` — documented once in [`_overview.md`](_overview.md)._

| Option | Required | Description |
|---|---|---|
| `--symbol` | Yes | Fully-qualified interface or abstract member name |

### Output Schema (`--format json`)

Grouped per matched symbol; each `result` is the list of implementing symbols.

```json
{
  "solutionRoot": "/abs/path/to/repo",
  "items": [
    {
      "symbol": "MyApp.Interfaces.IOrderProcessor",
      "kind": "interface",
      "result": [
        {
          "name": "OrderService",
          "fullName": "MyApp.Services.OrderService",
          "kind": "class",
          "file": "src/Services/OrderService.cs",
          "line": 12,
          "col": 14,
          "containingType": null,
          "containingNamespace": "MyApp.Services"
        }
      ]
    }
  ]
}
```

---
