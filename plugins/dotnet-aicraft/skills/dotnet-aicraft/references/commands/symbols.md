## `dotnet aicraft symbols`

Search for symbols by name pattern. Supports glob-style wildcards (`*`, `?`).

### Options

_Plus the global options — `-s`/`--solution`, `-p`/`--project`, `--format`, `--idle-timeout`, `--debug` — documented once in [`_overview.md`](_overview.md)._

| Option | Required | Description |
|---|---|---|
| `--pattern` | Yes | Name pattern with optional `*` and `?` wildcards |
| `--kind` | No | Filter by kind. Coarse: `all`, `type`, `member`, `namespace`. Granular: `class`, `interface`, `struct`, `enum`, `delegate`, `method`, `constructor`, `property`, `field`, `event`. Default: `all` |
| `--limit` | No | Max items per page (default: 200, max: 2000) |
| `--offset` | No | Skip first N matches (for pagination, default: 0) |

### Examples

```bash
dotnet aicraft symbols --solution App.sln --pattern "Process*" --kind method
dotnet aicraft symbols --solution App.sln --pattern "I*" --kind interface
dotnet aicraft symbols --solution App.sln --pattern "*" --kind all --limit 100 --offset 200

# Constructors of a class — --pattern matches the TYPE name, results expand to its constructors
dotnet aicraft symbols --solution App.sln --pattern "OrderService" --kind constructor
```

### Addressing constructors

`--kind constructor` matches the **type** name (not the constructor) and expands to that
type's constructors — so `--pattern "OrderService"` returns the constructors of any type whose
name contains `OrderService` (disambiguate via the fully-qualified names in the output).

To target a constructor in `refs`/`callers`/`definition`/`rename --symbol`, use the repeated
type name `Ns.Type.Type`, with parameters `Ns.Type.Type(System.String)`, or `Ns.Type.#ctor`.
Note the asymmetry: this listing omits a class's implicit default constructor, but
`refs`/`callers --symbol "Ns.Type.Type"` still resolves it.

### Output Schema (`--format json`)

```json
{
  "solutionRoot": "/abs/path",
  "items": [
    {
      "name": "ProcessOrder",
      "fullName": "MyApp.Services.OrderService.ProcessOrder(MyApp.Contracts.OrderRequest)",
      "kind": "method",
      "file": "src/Services/OrderService.cs",
      "line": 42,
      "col": 18,
      "containingType": "MyApp.Services.OrderService",
      "containingNamespace": "MyApp.Services"
    }
  ],
  "hasMore": true
}
```

---
