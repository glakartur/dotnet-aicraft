# Optional `--solution` Argument via CWD Auto-Discovery

**Date:** 2026-05-21
**Status:** Requirements
**Scope:** Lightweight

## Problem

Every `dotnet aicraft` command currently requires `--solution`/`-s`. When the user runs the CLI from inside a repo that already contains exactly one solution/project file, typing the path is redundant friction — especially for AI agents and humans iterating in the same folder.

## Goal

Make the solution/project argument optional. If omitted, auto-discover a single `.slnx`/`.sln`/`.csproj` in the current working directory and use it. Keep behavior identical when the argument is provided explicitly.

## Behavior

### Discovery rules

When no solution/project argument is provided, scan the current working directory (non-recursive) in this priority order:

1. `*.slnx`
2. `*.sln`
3. `*.csproj`

Search the **first tier that contains any matches** and resolve from there:

- **Exactly one match in the first non-empty tier** → use it.
- **Multiple matches in the first non-empty tier** → error: list candidates and instruct user to pass the argument explicitly.
- **No matches in any tier** → error: instruct user to pass `--solution`/`--project` or `cd` into a folder containing one.

Do **not** walk up the directory tree. CWD-only keeps the rule predictable; users who want a different folder pass the path explicitly.

### CLI surface

`--solution`/`-s` continues to accept any supported file (`.sln`, `.slnx`, `.csproj`, `.vbproj`, `.fsproj`) — already true today, only the help text hides it.

Add `--project`/`-p` as a **separate** option (not a System.CommandLine alias) so we can detect and error on conflicts. Both options accept the same file types and feed the same resolved path downstream.

Update help descriptions:
- `--solution`/`-s`: *"Path to the .sln/.slnx file (also accepts .csproj/.vbproj/.fsproj). Optional — auto-discovered from the current directory when omitted."*
- `--project`/`-p`: *"Path to the .csproj/.vbproj/.fsproj file (also accepts .sln/.slnx). Optional — auto-discovered from the current directory when omitted."*

### Resolution precedence

1. **Both `--solution` and `--project` provided with different paths** → error `CONFLICTING_PATH_ARGUMENTS`, exit non-zero.
2. **Both provided with the same path** → accept, use it.
3. **Exactly one of them provided** → use it.
4. **Neither provided** → run CWD auto-discovery (see Discovery rules).

When a path is provided through either flag, discovery is skipped entirely — current loader behavior unchanged.

## Prerequisites

- **`.slnx` loader support.** `src/DotnetAICraft/Roslyn/WorkspaceLoader.cs:34` currently dispatches only on `ext == ".sln"`. Extend the branch so `.slnx` also routes through `OpenSolutionAsync` (Roslyn 5.3 supports it). Without this, `.slnx` discovery would resolve a file the loader then rejects.

## Out of Scope

- Walking up parent directories.
- Environment variable (`DOTNET_AICRAFT_SOLUTION`) or config file fallback.
- Auto-picking among multiple matches by heuristic (name match, mtime, etc.).
- Recursive search.
- Renaming `--solution` away (kept for backward compatibility).

## Success Criteria

- Running `dotnet aicraft symbols --pattern Foo` from a folder with one `.sln`, `.slnx`, or `.csproj` works without any path argument.
- A folder containing both a `.sln` and a `.csproj` resolves to the `.sln` (tier priority).
- A folder with two `.sln` files prints a clear error listing both candidates and exits non-zero.
- A folder with no supported files prints a clear error and exits non-zero.
- `--solution`/`-s` and the new `--project`/`-p` both work and accept the same file types.
- Passing both `--solution` and `--project` with different paths errors with `CONFLICTING_PATH_ARGUMENTS`; passing both with the same path is accepted.
- `--help` reflects that the path is optional and describes discovery + supported extensions.

## Implementation Notes (for planning)

- `src/DotnetAICraft/Program.cs:39` — `solutionOption.Required = true` flips to `false`; add a second `Option<FileInfo>` for `--project`/`-p` (also optional). Each command's `Build` signature changes to take both options.
- Each command's `Entry.ExecuteAsync` accepts both `FileInfo?` values, then calls a shared resolver: returns the chosen path or writes a `CONFLICTING_PATH_ARGUMENTS` / `SOLUTION_NOT_FOUND` / `SOLUTION_AMBIGUOUS` error and returns null.
- `src/DotnetAICraft/Roslyn/WorkspaceLoader.cs:34` — extend the `.sln` branch to also accept `.slnx`.
- Discovery logic belongs in a shared helper (likely `Commands/Shared/`), invoked by each command's `Entry` before `CommandHelpers.SendWithRetryOrWriteErrorAsync` when the option value is null.
- Use the existing error envelope (`CommandHelpers.WriteError`) with new codes like `SOLUTION_NOT_FOUND` / `SOLUTION_AMBIGUOUS` for the two failure modes.

## Open Questions

None blocking. Planning can proceed.
