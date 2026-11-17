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

A fallback for when results look stale. The daemon keeps itself in sync:

- Edits and added, deleted, renamed or moved `.cs` files (also whole directories)
  are applied in memory within about a second. The watcher ignores `obj/`, `bin/`,
  hidden directories and `*.g.cs`.
- Changes to `.sln`, `.slnx`, `.csproj`, `Directory.Build.props/targets` or
  `Directory.Packages.props` trigger an automatic full reload. So does a new file
  whose project cannot be determined safely (legacy or nested projects, explicit
  `Compile` items), a large batch of added/removed files, and a file-watcher error.
  During a reload, queries return `SOLUTION_LOADING`.
- `rename` right after a file was added or deleted can return `SOLUTION_LOADING`
  (it reloads first, so it never writes project files). Retry after the reload.

Run `server reload` when the watcher can miss events, e.g. on network drives or
WSL2 with files under `/mnt`.

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
