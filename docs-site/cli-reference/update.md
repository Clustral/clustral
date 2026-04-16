---
description: Self-update the `clustral` binary by downloading the latest release from GitHub.
---

# `clustral update`

Check GitHub Releases for a newer `clustral` binary and replace the current executable in place.

## Synopsis

```
clustral update [--pre] [--check]
```

## Description

`update` queries `api.github.com/repos/Clustral/clustral/releases/latest` (or `/releases` when `--pre` is set), picks the asset that matches your OS and architecture (e.g., `clustral-darwin-arm64`, `clustral-linux-amd64`, `clustral-windows-amd64.exe`), downloads it, and atomically replaces the currently-running binary.

Replacement is done with the rename-then-swap pattern:

1. Download to `<path>.new`.
2. On Unix, `chmod 755`.
3. Move current binary to `<path>.old`.
4. Move `.new` to `<path>`.
5. Delete `.old`.

If the running version already matches the latest release, the command prints `Already up to date.` and exits 0 without touching the binary.

{% hint style="warning" %}
Homebrew, apt, or other package-managed installs should be updated through the package manager, not through `clustral update`. Overwriting a managed install can leave the package database out of sync.
{% endhint %}

## Options

| Flag | Description | Default |
|---|---|---|
| `--pre` | Consider pre-release versions (alpha, beta, rc channels). | `false` |
| `--check` | Only check — print current and latest versions, do not download. | `false` |

## Examples

### Up to date

```bash
$ clustral update
Current version: v0.4.1
Latest version:  v0.4.1
Already up to date.
```

### Update available, apply it

```bash
$ clustral update
Current version: v0.4.0
Latest version:  v0.4.1
Updated to v0.4.1.
```

### Check only

```bash
$ clustral update --check
Current version: v0.4.0
Latest version:  v0.4.1
Update available: v0.4.0 → v0.4.1
```

### Follow the pre-release channel

```bash
$ clustral update --pre
Current version: v0.4.1
Latest version:  v0.5.0-alpha.2
Updated to v0.5.0-alpha.2.
```

### No binary for your platform

```bash
$ clustral update
Current version: v0.4.0
Latest version:  v0.4.1
✗ No binary published for clustral-freebsd-arm64 in release v0.4.1.
```

### Scripting

```bash
# Nightly self-update via cron, pre-release channel.
$ clustral update --pre >> /var/log/clustral-update.log 2>&1
```

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `No releases found.` | GitHub API returned an empty payload. | Check rate limit (`https://api.github.com/rate_limit`); retry. |
| `Cannot determine binary path.` | Running under an environment that does not expose `Environment.ProcessPath`. | Download manually from the release page. |
| `Permission denied` on rename | The binary is on a read-only filesystem (e.g., `/usr/local/bin` without sudo). | Re-run with appropriate privileges or install to a user-writable path. |
| Update succeeded but `clustral version` still shows the old version | A wrapper script or alias points at the old path. | Resolve with `which -a clustral`; update `$PATH` or the alias. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Already up to date, or successfully updated, or `--check` finished. |
| 1 | Network failure, missing asset, or binary path could not be determined. |

## See also

- [`clustral version`](version.md) — inspect the running CLI version.
- [GitHub Releases](https://github.com/Clustral/clustral/releases) — browse changelogs and download binaries manually.
