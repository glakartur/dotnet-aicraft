## `dotnet aicraft rename`

Safely rename a symbol across the entire solution. All call sites, declarations, and XML docs are updated atomically.

### Options

_Plus the global options — `-s`/`--solution`, `-p`/`--project`, `--format`, `--idle-timeout`, `--debug` — documented once in [`_overview.md`](_overview.md)._

| Option | Required | Description |
|---|---|---|
| `--file` | If not using `--symbol` | Source file path |
| `--line` | With `--file` | 1-based line number |
| `--col` | With `--file` | 1-based column number |
| `--symbol` | If not using `--file` | Fully-qualified symbol name |
| `--to` | Yes | New name (just the identifier, not fully-qualified) |
| `--dry-run` | No | Preview changes without applying |

### Examples

```bash
# Preview first (always recommended)
dotnet aicraft rename --solution App.sln \
  --symbol "MyApp.Services.OrderService.ProcessOrder" \
  --to "HandleOrder" --dry-run

# Apply rename
dotnet aicraft rename --solution App.sln \
  --symbol "MyApp.Services.OrderService.ProcessOrder" \
  --to "HandleOrder"
```

### Output Schema (`--format json`)

```json
{
  "symbol": "MyApp.Services.OrderService.ProcessOrder",
  "newName": "HandleOrder",
  "applied": false,
  "dryRun": true,
  "changes": [
    {
      "file": "src/Services/OrderService.cs",
      "line": 42,
      "col": 17,
      "oldText": "ProcessOrder",
      "newText": "HandleOrder"
    }
  ]
}
```

- `applied`: `true` when changes were written to disk
- `dryRun`: mirrors the `--dry-run` flag
- `changes`: list of all affected locations (paths relative to solution root)

---
