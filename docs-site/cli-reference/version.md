---
description: Print the CLI version and, when a ControlPlane is configured, the ControlPlane version.
---

# `clustral version`

Show the version of the `clustral` binary and the ControlPlane it points at.

## Synopsis

```
clustral version [--json]
```

## Description

The CLI reads its own version from the embedded `AssemblyInformationalVersion` attribute — the same string `git describe --tags` produced at build time (local dev builds show `0.0.0-dev`).

If `~/.clustral/config.json` has a ControlPlane URL, the CLI also calls `GET /api/v1/version` on the ControlPlane (an unauthenticated endpoint) and prints the server version alongside. If the URL is not configured, the server line is omitted. If the server is configured but unreachable, the server line prints `(version unknown)` and the command still exits 0.

## Options

No command-specific flags. The global `--json` flag (see [Overview](README.md)) switches to machine-readable output. `--insecure` is honored via the `insecureTls` field in `~/.clustral/config.json`.

| Flag | Description | Default |
|---|---|---|
| `--json` | Emit machine-readable JSON. | `false` |

## Examples

### Standard output

```bash
$ clustral version
CLI            v0.4.1
ControlPlane    v0.4.1
```

### Not yet configured

```bash
$ clustral version
CLI            v0.4.1
ControlPlane    (not configured — run 'clustral login <url>')
```

### ControlPlane unreachable

```bash
$ clustral version
CLI            v0.4.1
ControlPlane    (version unknown)
```

### Machine-readable

```bash
$ clustral version --json
{"cli":"v0.4.1","controlPlane":"v0.4.1"}

$ clustral version --json | jq -r '.controlPlane'
v0.4.1
```

### Version skew check

```bash
$ test "$(clustral version --json | jq -r .cli)" = \
       "$(clustral version --json | jq -r .controlPlane)" \
  && echo matched \
  || echo mismatched
matched
```

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Output rendered (even when the ControlPlane is unreachable). |
| 1 | Generic I/O error. |

## See also

- [`clustral doctor`](doctor.md) — full connectivity diagnostics including ControlPlane health.
- [`clustral update`](update.md) — upgrade the CLI to the latest release.
- [`clustral status`](status.md) — session, kubeconfig, and ControlPlane reachability in one view.
