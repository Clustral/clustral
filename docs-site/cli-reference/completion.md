---
description: Generate static shell completion scripts for bash, zsh, or fish.
---

# `clustral completion`

Print a shell completion script for bash, zsh, or fish. Pipe the output into your shell's completion system.

## Synopsis

```
clustral completion <shell>
```

`<shell>` is one of `bash`, `zsh`, `fish`.

## Description

The scripts are fully static — they register the `clustral` command tree (subcommands, options, and arguments) with the shell's completion system at source time. There is no runtime `dotnet-suggest` dependency, no JSON parsing, and no process invocation on each `Tab` press.

Completions cover:

- Top-level commands (`login`, `logout`, `kube`, `clusters`, `users`, `roles`, `access`, `audit`, `config`, `status`, `doctor`, `profiles`, `accounts`, `whoami`, `update`, `version`, `completion`).
- Subcommands for each parent (e.g., `kube login`, `kube logout`, `access request`, `access approve`).
- Common flag values (`--output table|json`).

Dynamic completions (cluster names, role names, request IDs) are not supported — they would require network calls on every `Tab`.

## Options

No command-specific flags. Unknown shell names print an error and exit 1.

## Examples

### bash

```bash
# One-shot (current shell only):
eval "$(clustral completion bash)"

# Persistent — add to ~/.bashrc:
echo 'eval "$(clustral completion bash)"' >> ~/.bashrc
source ~/.bashrc
```

### zsh

```zsh
# One-shot:
source <(clustral completion zsh)

# Persistent — add to ~/.zshrc:
echo 'eval "$(clustral completion zsh)"' >> ~/.zshrc
source ~/.zshrc
```

Zsh requires the `compinit` autoloader. If completions do not work, ensure your `~/.zshrc` contains:

```zsh
autoload -Uz compinit && compinit
```

### fish

```fish
# Save to the user completions directory — loaded automatically on next shell:
clustral completion fish > ~/.config/fish/completions/clustral.fish
```

### Verify it works

```bash
$ clustral <Tab>
access      audit       clusters    completion  config      doctor
kube        login       logout      profiles    roles       status
update      users       version     whoami      accounts

$ clustral kube <Tab>
list    login   logout  ls
```

### Inspect the script

```bash
$ clustral completion bash | head -5
# clustral bash completion — add to ~/.bashrc:
#   eval "$(clustral completion bash)"

_clustral_completions() {
    local cur prev commands
```

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Script written to stdout. |
| 1 | Unknown shell name. |

## See also

- [`clustral version`](version.md) — check whether your CLI is new enough to know about newer subcommands.
- [`clustral update`](update.md) — upgrade the CLI, then regenerate completions.
