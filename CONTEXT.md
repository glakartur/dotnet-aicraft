# dotnet-aicraft

Semantic .NET code analysis exposed to AI agents as CLI commands. Each command answers one
question an agent asks while coding; this glossary fixes what each term means so commands stay
distinct and non-overlapping.

## Language

### Single-symbol / container inspection

These four verbs each answer a *different question* about a symbol or a container. They overlap
only in the fields they emit, never in intent.

**Definition**:
Where a symbol is declared. A lean locator (location + identity) used as a primitive in chains —
resolve a name, take the location, feed it to another command. Stays minimal on purpose.
_Avoid_: declaration, locate

**Describe**:
What a symbol *is* — its semantic card: signature, return/parameter types, modifiers, attributes,
XML-doc, and overloads. The command an agent reaches for to understand a symbol without reading
its file.
_Avoid_: info, hover, detail

**Outline**:
What is *declared inside* a container (a file or a type) — its own members with signatures and
line numbers, but no bodies. Declared-only by default; `--include-inherited` additionally lists
base-class-chain members grouped by declaring type. Lets an agent grasp structure without
reading the whole file.
_Avoid_: structure, members, document-symbols, toc

**Source**:
The verbatim text of a symbol's declaration (body included) plus its span. Lets an agent read one
symbol instead of guessing line ranges over a whole file.
_Avoid_: body, snippet, code
