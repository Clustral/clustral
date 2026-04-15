---
description: How to contribute to the Clustral documentation — style, structure, and preview workflow.
---

# Contributing to the docs

This page is the authoring contract for every page under `docs-site/`. Follow it so pages stay consistent across contributors and publication on GitBook.

## Source of truth

- **The repository is canonical.** All pages are plain Markdown under `docs-site/` and flow to GitBook via Git-Sync. Edits can originate in the repo or in the GitBook UI — either way, the repo is the version of record.
- **Error pages under `docs-site/errors/` are hand-maintained.** There is no generator. When you add or rename an error code in `packages/Clustral.Sdk/Results/ResultErrors.cs`, add or rename the corresponding page in the same PR.

## Page anatomy

Every non-index page uses this structure:

```markdown
---
description: One-sentence summary (≤160 chars, shown in search results and link unfurls).
---

# Page Title

One-sentence lede that answers "what is this page for" — readable standalone.

## Overview

One to two paragraphs of context. Link to related concepts.

{% hint style="info" %}
Optional: prerequisites, compatibility, version notes.
{% endhint %}

## <Main content heading>

Steps, tables, diagrams, code samples.

## Examples

At least one real, copy-pasteable example per how-to page.

## Troubleshooting

Symptom → cause → fix table for anything that fails in the wild.

## See also

- [Related page](../path/to/page.md)
- [Related concept](../other/path.md)
```

Section overview pages (`<section>/README.md`) can skip the lede and go straight to a short intro + link list — they are navigation, not content.

## Voice and style

- **"You," not "we."** Address the reader directly. Avoid marketing first-person plural.
- **Present tense.** "Clustral issues an internal JWT…" not "will issue."
- **Declarative, terse.** One idea per sentence. Cut hedges ("basically," "essentially," "typically").
- **No marketing fluff.** Docs exist to help the reader get something done. Skip adjectives that don't carry information ("seamless," "powerful," "robust").
- **Absolute paths over relative language.** "Edit `src/Clustral.ControlPlane/appsettings.json`" beats "edit the appsettings file in the ControlPlane."
- **Copy-pasteable code.** Every shell command, config snippet, and `curl` example in the docs should run as-is against a default install. If variables are required, mark them clearly (`<your-cluster-id>`).
- **Version assumptions are explicit.** If a feature only works on v0.3+, say so in a callout.

## Callouts

Use GitBook hint blocks sparingly — one, maybe two per page. They should carry information the reader would otherwise miss, not emphasize ordinary prose.

```markdown
{% hint style="info" %}
Prerequisite or compatibility note.
{% endhint %}

{% hint style="warning" %}
Watch-out — something non-obvious that commonly goes wrong.
{% endhint %}

{% hint style="danger" %}
Destructive operation or security-sensitive step.
{% endhint %}

{% hint style="success" %}
Positive confirmation (rarely useful; prefer a normal sentence).
{% endhint %}
```

## Tabs

Use `{% tabs %}` only when content genuinely differs per platform or audience — e.g., install commands by OS, connection strings by client library.

```markdown
{% tabs %}
{% tab title="macOS" %}
```bash
brew install clustral
```
{% endtab %}

{% tab title="Linux" %}
```bash
curl -sL https://get.clustral.io | sh
```
{% endtab %}
{% endtabs %}
```

## Cross-links

Link generously — roughly one cross-link per 200–400 words of prose. Prefer relative paths (`../architecture/authentication-flows.md`) so they work both on GitBook and in GitHub rendering. Every page ends with a `## See also` list of 2–4 related pages.

## Diagrams

- **Mermaid fenced blocks** are the default. They render in GitHub, VS Code, and most GitBook configurations.
- **Images** live in `docs-site/.gitbook/assets/`. Reference with relative paths: `![Auth flow](.gitbook/assets/auth-flow.svg)`.
- **Avoid screenshots of the Web UI** until the UI design has stabilized. When you add one, commit a 1× and a 2× version, or use SVG.

## Previewing your changes

- **GitHub rendering** — push your branch and open the file on github.com; this is the most accurate proxy for how GitBook will render callouts, tabs, and Mermaid.
- **VS Code** — use the built-in Markdown preview. Install "Markdown Preview Enhanced" if you want Mermaid to render locally.
- **GitBook** — once the space is provisioned, the branch preview URL is the source of truth for final rendering.

## Checks before you open a PR

- Every new page has a YAML `description:` in front-matter.
- Every non-index page has a lede sentence under the H1.
- Every how-to page has at least one runnable example.
- Every page ends with `## See also`.
- All relative links resolve (run `grep -rn '](\.\./' docs-site/` and spot-check).
- Code samples execute cleanly against a default install (paste-test the ones you added).
- No TODO/FIXME placeholders remain.

## When to update `SUMMARY.md`

Whenever you add, remove, or rename a page. `SUMMARY.md` is the navigation tree GitBook uses to order pages in the sidebar. If you forget to update it, the new page won't appear in navigation (even though GitBook will still serve it by URL).

## When to update `.gitbook.yaml`

Only when you rename or move a page whose URL is part of the public contract — most notably anything under `/errors/<code>`, which the platform emits in RFC 7807 `type` fields and `Link: rel="help"` headers. Add a `redirects:` entry mapping the old path to the new one.
