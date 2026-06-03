## `dotnet aicraft unused`

Find symbols with no references — candidates for dead code.

### Options

_Plus the global options — `-s`/`--solution`, `-p`/`--project`, `--format`, `--idle-timeout`, `--debug` — documented once in [`_overview.md`](_overview.md)._

| Option | Required | Description |
|---|---|---|
| `--kind` | No | Restrict to a kind: `all` (default), `type`, `member`, `namespace`, `class`, `interface`, `struct`, `enum`, `delegate`, `method`, `constructor`, `property`, `field`, `event` |
| `--project-name` | No | Restrict results to a single project by name |
| `--public-only` | No | Only include public symbols |
| `--include-generated` | No | Include generated code (default: skipped) |

### Output Schema (`--format json`)

```json
{
  "solutionRoot": "/abs/path",
  "scanned": 1234,
  "items": [
    { "symbol": "MyApp.Internal.LegacyHelper.DoStuff",
      "kind": "method", "reason": "no references",
      "confidence": "high",
      "file": "src/Internal/LegacyHelper.cs", "line": 17, "col": 22 }
  ]
}
```

---
