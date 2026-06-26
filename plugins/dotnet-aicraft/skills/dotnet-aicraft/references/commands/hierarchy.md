## `dotnet aicraft hierarchy`

Type inheritance lineage — the counterpart to `impls`. Where `impls` answers *who realizes this
contract*, `hierarchy` answers *what is this type's inheritance lineage*: its base types (`up`) or
its derived types (`down`), transitively, across every project in the solution, and generics-aware.

Targets a class, struct, interface, or record. Each matched type gets one `HierarchyNode` tree root
(the target itself), wrapped in a result group like `impls`/`callers`. A grep for `: BaseName`
can't do this — it misses transitive ancestors/descendants, generic constructions (`: Box<int>`),
and cross-project edges, and it produces false positives.

### Options

_Plus the global options — `-s`/`--solution`, `-p`/`--project`, `--format`, `--idle-timeout`, `--debug` — documented once in [`_overview.md`](_overview.md)._

| Option | Required | Description |
|---|---|---|
| `--symbol` | If not using `--file` | Fully-qualified type name |
| `--file` | If not using `--symbol` | Source file path |
| `--line` | With `--file` | 1-based line number |
| `--col` | With `--file` | 1-based column number |
| `--direction` | **Yes** | `up` (base types) or `down` (derived types). No default. |
| `--include-framework` | No | In `up`, continue through BCL/framework metadata bases up to `object` (omitted by default) |
| `--max-depth` | No | Cap traversal depth (min 1; default: no cap). Nodes whose children are elided are marked `truncated` |

### Direction semantics

- **class / struct / record, `up`** — the base-class chain only (implemented interfaces are *not*
  included — that's a separate question; use `impls` / interface queries). `struct`/`record` behave
  as their class form.
- **class / struct / record, `down`** — transitively derived classes (a `struct` is sealed → empty).
- **interface, `up`** — the interfaces it extends.
- **interface, `down`** — derived **interfaces only**. Implementing classes deliberately stay the
  job of `impls`, so the two commands never overlap.

By default `up` stops at (and omits) the first non-source base — BCL/framework types you usually
don't care about. `--include-framework` walks through them up to `object`; those metadata nodes have
no source location (empty `file`, zero `line`/`col`), rendered location-less in text. Generics are
matched semantically and node identity shows the *constructed* form (`Box<string>`, not `Box<T>`).

### Examples

```bash
# Base-type chain (up) — framework bases omitted
dotnet aicraft hierarchy --solution App.sln \
  --symbol "MyApp.Animals.Puppy" --direction up

# Continue the chain through framework bases, up to object
dotnet aicraft hierarchy --solution App.sln \
  --symbol "MyApp.Animals.Puppy" --direction up --include-framework

# Derived types (down), transitive across projects
dotnet aicraft hierarchy --solution App.sln \
  --symbol "MyApp.Animals.Animal" --direction down

# Cap depth; deeper nodes are marked truncated, never silently dropped
dotnet aicraft hierarchy --solution App.sln \
  --symbol "MyApp.Animals.Animal" --direction down --max-depth 2

# By file location instead of --symbol
dotnet aicraft hierarchy --solution App.sln \
  --file src/Animals/Animal.cs --line 5 --col 18 --direction down
```

### Output Schema (`--format json`)

Grouped per matched symbol; each `result` is the root `HierarchyNode`, with immediate bases/deriveds
nested under `children[]` (recursively). A leaf has `"children": []`; a node capped by `--max-depth`
carries `"truncated": true` with empty children.

```json
{
  "solutionRoot": "/abs/path",
  "items": [
    {
      "symbol": "MyApp.Animals.Animal",
      "kind": "class",
      "result": {
        "name": "Animal",
        "fullName": "MyApp.Animals.Animal",
        "kind": "class",
        "file": "src/Animals/Animal.cs",
        "line": 5,
        "col": 18,
        "containingType": null,
        "containingNamespace": "MyApp.Animals",
        "truncated": false,
        "children": [
          {
            "name": "Dog",
            "fullName": "MyApp.Animals.Dog",
            "kind": "class",
            "file": "src/Animals/Dog.cs",
            "line": 3,
            "col": 18,
            "containingNamespace": "MyApp.Animals",
            "truncated": false,
            "children": []
          }
        ]
      }
    }
  ]
}
```

### Errors

- Omitting `--direction`, an unknown direction, or `--max-depth < 1` → `INVALID_PARAMS` (with the
  accepted values / minimum in the details).
- A resolved enum, delegate, namespace, or member → `INVALID_TARGET_KIND` naming the resolved kind
  and the accepted kinds (class, struct, interface, record) — rather than a misleading empty tree.

---
