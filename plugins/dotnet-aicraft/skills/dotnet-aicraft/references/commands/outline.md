## `dotnet aicraft outline`

The members a container declares — its own methods, properties, fields, events, constructors, and
nested types (and their members) — as flat located lines with signatures but no bodies. Lets an agent
grasp structure without reading the whole file. Declared-only by default, including `private` members;
pass `--public-only` to restrict to the consumable/extensible surface.

### Options

_Plus the global options — `-s`/`--solution`, `-p`/`--project`, `--format`, `--idle-timeout`, `--debug` — documented once in [`_overview.md`](_overview.md)._

`outline` diverges from the shared location contract: it takes **`--symbol <type>` XOR a bare `--file <path>`**, and rejects `--line`/`--col`.

| Option | Required | Description |
|---|---|---|
| `--symbol` | If not using `--file` | Fully-qualified **type** name to outline |
| `--file` | If not using `--symbol` | Source file — outlines every top-level type it declares |
| `--public-only` | No | List only the consumable/extensible surface (see below) |
| `--include-inherited` | No | Also list base-class-chain members, grouped by declaring type |

`--public-only` keeps `public`, `internal`, `protected`, and `protected internal` and drops `private`
and `private protected`. It means "what a consumer or a subclass author can see", **not** literally
public-only — the name is shorthand for that extensible surface.

`--include-inherited` walks the **base-class chain only** (interfaces are `impls`/`hierarchy`
territory). Inherited members render grouped under a `inherited from <type> [<assembly>]:` header, so
`System.Object` members sit cheaply at the bottom rather than being dropped. An inherited member that a
declared member overrides is suppressed (the override shows as the declared located line); an inherited
member shadowed by a declared `new` member is shown and tagged `(hidden by new)`.

`outline --symbol` on a method/property returns `INVALID_PARAMS` pointing to `describe`; on a namespace,
pointing to `symbols`. A `--file` with no type declarations returns an empty, successful result.

### Output (`--format text`)

```
match: class MyApp.Services.Widget
outline:
src/Services/Widget.cs:12:18: public Widget(string name)
src/Services/Widget.cs:15:17: private readonly string _name
src/Services/Widget.cs:18:21: public string Render()
inherited from object [System.Private.CoreLib]:
  public virtual string ToString()
  public virtual bool Equals(object? obj)
```

Nested members carry their declaring type (e.g. `[MyApp.Services.Widget.Inner]`) so they stay
unambiguous.

### Output Schema (`--format json`)

Grouped per container (a `--file` with several top-level types yields one group each).

```json
{
  "solutionRoot": "/abs/path/to/repo",
  "items": [
    {
      "symbol": "MyApp.Services.Widget",
      "kind": "class",
      "result": {
        "container": "MyApp.Services.Widget",
        "kind": "class",
        "publicOnly": false,
        "includeInherited": true,
        "declared": [
          { "file": "src/Services/Widget.cs", "line": 18, "col": 21, "declaringType": "MyApp.Services.Widget", "signature": "public string Render()" }
        ],
        "inherited": [
          { "declaringType": "object", "assembly": "System.Private.CoreLib", "members": [ { "signature": "public virtual string ToString()" } ] }
        ]
      }
    }
  ]
}
```

---
