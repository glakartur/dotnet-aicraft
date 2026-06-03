## `dotnet aicraft server`

Manage the background analysis daemon.

### `server status`

```bash
dotnet aicraft server status --solution App.sln
```

Returns daemon state, project/document counts, current idle timeout, uptime.

### `server start`

`server start` is a **fast, idempotent ensure-running** call. It returns
promptly: with no running daemon it spawns one in the background and waits only
until the daemon is ready to accept commands; with a running daemon it extends
the idle deadline (no `--idle-timeout`) or overwrites the session idle timeout
(with `--idle-timeout`, equivalent to `server reload --idle-timeout`).

```bash
# Ensure the daemon is running
dotnet aicraft server start --solution App.sln

# Ensure running and set / extend the session idle timeout
dotnet aicraft server start --solution App.sln --idle-timeout 30m

# Disable auto-shutdown for this session
dotnet aicraft server start --solution App.sln --idle-timeout off
```

External scripts that previously relied on `server start` blocking until
shutdown should switch to a long-lived monitoring loop or use the daemon's
idle-timeout settings.

Note: Any analysis command also starts the daemon if not already running.
The internal foreground daemon process is reachable via a hidden
`server daemon` subcommand — it is for internal auto-spawn use only and not
intended for direct invocation.

### `server stop`

```bash
dotnet aicraft server stop --solution App.sln
```

Sends a graceful shutdown signal. The next analysis command will restart the daemon.

### `server reload`

```bash
dotnet aicraft server reload --solution App.sln
```

Use after adding or removing `.csproj` files from the solution. Normal `.cs`
file changes are picked up automatically via the file watcher (which ignores
`obj/`, `bin/`, and `*.g.cs`).

### Windows stale-socket self-heal

On Windows, daemon startup applies a bounded stale-artifact policy before bind:
regular-file stale sockets are auto-removed; reparse-point artifacts are
removed only when every safety gate passes (local non-UNC target, target under
the current user's `%TEMP%` root, `dotnet-aicraft-*.sock` naming, supported
reparse tag). Any failure is fail-closed and returns:

- `error.code = DAEMON_STARTUP_STALE_SOCKET_INVALID_TYPE`
- `error.details.reasonCode` (e.g. `outsideTempRoot`, `nonLocalTarget`, `unsupportedReparseTag`)
- `error.details.remediation` with Windows-safe manual cleanup guidance

Diagnostics are sanitized — no absolute local paths are surfaced.

---
