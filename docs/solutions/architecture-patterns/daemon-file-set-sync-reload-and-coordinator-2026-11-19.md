---
title: "Daemon file-set auto-sync: reload triggers, ownership fallback and WorkspaceSyncCoordinator"
date: 2026-11-19
category: architecture-patterns
module: AICraftDaemon
problem_type: architecture_pattern
component: tooling
severity: medium
applies_when:
  - "Changing watcher handling, automatic full reload or .cs file-set sync in the daemon"
  - "Adding a new trigger that can start a full MSBuild load"
  - "Deciding when a new .cs file may be added without a full reload"
tags: [daemon, file-watcher, msbuild, reload, retry, ownership-resolver, refactoring]
---

# Daemon file-set auto-sync: reload triggers, ownership fallback and WorkspaceSyncCoordinator

## Context

The branch adds automatic `.cs` file-set sync to the daemon (requirements: `docs/brainstorms/2026-11-15-daemon-file-set-auto-sync-requirements.md`, plan: `docs/plans/2026-11-17-001-feat-daemon-file-set-auto-sync-plan.md`). Review of PR #1 produced four decisions (D1-D4) that fix the rules below. Verification by a human stays required (AI output).

## Guidance

1. **Reload only for the registered watcher (D1).** On a watcher `Error`, call `ScheduleFullReload` only if the sender is the currently registered watcher (`IsRegisteredWatcher`, reference equality). Errors from a watcher that is still starting, or from a replaced one, are only logged. Otherwise a start-up error causes a second full MSBuild load right after the first, and queries get `SOLUTION_LOADING` again. Test: `StartWorkspaceServices_UnreadableSubdirectory_DoesNotRecurseOrHang` checks `LoadAttemptCount == 1`.
2. **Narrow non-candidate fallback (D2).** In `ProjectOwnershipResolver`, a project that is not the owner candidate forces `Fallback` (full reload) only for an explicit `<Compile Include>` or `<Compile Link>` that points outside that project's directory. `Compile Update/Remove` and output-path properties of other projects are ignored. A broad rule made every new `.cs` file cause a full reload in typical multi-project solutions (weakens R1). Resolver tests cover both cases.
3. **Bounded retry after a failed automatic reload (D3).** A failed reload (half-written `.csproj`, file lock) must not leave the daemon `Unloaded` with `_solution = null`. Call `ScheduleFullReload` again: max 3 attempts, exponential backoff with jitter, counter reset after success, log entry when attempts run out. Tests: retry budget and reset against a live solution (`DaemonFullReloadLiveTests`).
4. **Keep sync logic out of `DaemonServer` (D4).** `WorkspaceSyncCoordinator` owns watcher lifecycle, the file-set queue, `ScheduleFullReload` and the retry. `DaemonServer` stays the owner of `_solution`, `_workspace` and the lock. Coordinator start methods are private. D3 was implemented directly in the coordinator.

## Why This Matters

Each automatic trigger multiplies the cost of a wrong decision: an extra full load blocks queries, and an over-eager fallback removes the benefit of incremental sync. A 2800-line `DaemonServer` mixing five concerns made these rules hard to test.

## When to Apply

- A new watcher, metadata trigger or error path: check the "registered watcher" rule first.
- A new ownership rule: ask whether it forces a full reload for common solutions.
- Any new background reload path: route it through `ScheduleFullReload` so the retry budget applies.

## Examples

```csharp
// Only the registered watcher may plan a reload; others are logged.
if (!IsRegisteredWatcher(kind, sender)) { _log("ignored watcher error"); return; }
ScheduleFullReload();
```

## Related

- PR: https://github.com/glakartur/dotnet-aicraft/pull/1
- Commits: 7e23154, 338d0ac, a8ce385, a5fcb25
