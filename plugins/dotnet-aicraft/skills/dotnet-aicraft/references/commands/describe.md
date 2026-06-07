## `dotnet aicraft describe`

A semantic card for a symbol: `definition`'s location/identity **plus** signature, return/parameter
types, modifiers, attributes, cleaned XML-doc, and sibling overloads. The command to understand a
symbol without reading its file. Works on metadata symbols (BCL/NuGet) that have no source on disk.

### Options

_Plus the global options — `-s`/`--solution`, `-p`/`--project`, `--format`, `--idle-timeout`, `--debug` — documented once in [`_overview.md`](_overview.md)._

| Option | Required | Description |
|---|---|---|
| `--symbol` | If not using `--file` | Fully-qualified symbol name |
| `--file` | If not using `--symbol` | Source file path |
| `--line` | With `--file` | 1-based line number |
| `--col` | With `--file` | 1-based column number |

A fully-qualified name without a parameter signature can match several overloads — `describe` then
returns one group per overload. Each group's `siblings` list names the **other** overloads, never the
one the group is about. A namespace is not describable: `describe --symbol <namespace>` returns
`INVALID_PARAMS` pointing you to `symbols`. Generic types are addressed by `--file/--line/--col`
(the bare `--symbol` FQN form has no place for type arguments).

### Output Schema (`--format json`)

Grouped per matched symbol. Each `result` is a card. Null fields are omitted; `file/line/col` are
null for metadata-only symbols, which instead carry `assembly`.

```json
{
  "solutionRoot": "/abs/path/to/repo",
  "items": [
    {
      "symbol": "MyApp.Services.OrderService.Process(MyApp.OrderDto)",
      "kind": "method",
      "result": {
        "fullName": "MyApp.Services.OrderService.Process(MyApp.OrderDto)",
        "kind": "method",
        "file": "src/Services/OrderService.cs",
        "line": 42,
        "col": 18,
        "containingType": "MyApp.Services.OrderService",
        "containingNamespace": "MyApp.Services",
        "signature": "public async Task<int> Process(OrderDto dto)",
        "returnType": "Task<int>",
        "parameters": [ { "name": "dto", "type": "MyApp.OrderDto" } ],
        "modifiers": [ "async" ],
        "attributes": [ "Obsolete" ],
        "documentation": "Processes one order and returns its id.",
        "siblings": [ "public int Process(int orderId)" ]
      }
    }
  ]
}
```

For a **type**, the card carries the full type header in `signature` (accessibility + modifiers +
keyword + base/interfaces + type parameters/constraints) and **no** member list — use `outline` for
members. Enum members and `const` fields add `constantValue`. Metadata symbols add `assembly` and
omit `documentation` when the reference's `.xml` sidecar was not loaded.

---
