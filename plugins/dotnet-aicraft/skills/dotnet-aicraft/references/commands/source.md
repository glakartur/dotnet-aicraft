## `dotnet aicraft source`

The verbatim declaration text of a symbol — leading XML-doc + attributes + signature + body —
with its file and line span. Lets an agent read one symbol instead of guessing line ranges over a
whole file. A `partial` type/method (and each matched overload) yields one block per declaring part.

### Options

_Plus the global options — `-s`/`--solution`, `-p`/`--project`, `--format`, `--idle-timeout`, `--debug` — documented once in [`_overview.md`](_overview.md)._

| Option | Required | Description |
|---|---|---|
| `--symbol` | If not using `--file` | Fully-qualified symbol name |
| `--file` | If not using `--symbol` | Source file path |
| `--line` | With `--file` | 1-based line number |
| `--col` | With `--file` | 1-based column number |

Bodiless members (abstract/interface methods, auto-properties, `extern`, partial declarations) return
their declaration text up to the `;`. A symbol with no source on disk does **not** error: a
metadata-only symbol (BCL/NuGet) or a compiler-generated member (a record's synthesized members, an
implicit constructor) returns a successful result with `hasSource: false`, an empty `blocks` list, the
declaring `assembly`, and an explanatory `note`. Decompilation of metadata is out of scope.

### Output Schema (`--format json`)

Grouped per matched symbol (one group per overload). Each `result` is a `SourceResult`.

```json
{
  "solutionRoot": "/abs/path/to/repo",
  "items": [
    {
      "symbol": "MyApp.Widget",
      "kind": "class",
      "result": {
        "fullName": "MyApp.Widget",
        "kind": "class",
        "hasSource": true,
        "blocks": [
          { "file": "src/Widget.Part1.cs", "startLine": 5, "endLine": 20, "text": "public partial class Widget\n{ ... }" },
          { "file": "src/Widget.Part2.cs", "startLine": 3, "endLine": 11, "text": "public partial class Widget\n{ ... }" }
        ]
      }
    }
  ]
}
```

Metadata / generated degradation:

```json
{
  "fullName": "System.String.Substring(int)",
  "kind": "method",
  "hasSource": false,
  "blocks": [],
  "assembly": "System.Private.CoreLib",
  "note": "no source available — declared in metadata (System.Private.CoreLib)"
}
```

---
