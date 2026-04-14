# docs-gen

Generates the error-catalog Markdown pages in `docs-site/errors/` from the
Clustral SDK's `ResultErrors` catalog and the API Gateway's status-code
handlers.

## Usage

```bash
# Generate / update all error pages
dotnet run --project tools/docs-gen

# CI mode — generate + verify no stale pages exist
dotnet run --project tools/docs-gen -- --verify
```

## How it works

1. **Reflection** on `Clustral.Sdk.Results.ResultErrors` — invokes every
   `public static` method that returns `ResultError` with placeholder args
   to capture `Code`, `Kind`, and `Message`.

2. **Hardcoded gateway codes** — `RATE_LIMITED`, `ROUTE_NOT_FOUND`,
   `UPSTREAM_*`, `GATEWAY_ERROR`, `CLIENT_CLOSED`, `TIMEOUT`, etc. are
   emitted by the API Gateway and exception handler middleware, not by
   `ResultErrors` factories.

3. **Deduplication** — multiple factories may produce the same code (e.g.,
   `INVALID_TOKEN` from 7 different token-failure paths). The tool merges
   them into a single page, listing all `emitted_by` factories.

4. **Merge on re-run** — auto-generated content lives between
   `<!-- AUTO-GEN-START -->` and `<!-- AUTO-GEN-END -->` markers. Everything
   outside (the hand-authored "What this means" / "Why it happens" /
   "How to fix" sections) is preserved across re-runs.

## Output structure

```
docs-site/errors/
├── README.md                    ← index table of all codes
├── cluster-not-found.md
├── invalid-token.md
├── no-role-assignment.md
├── rate-limited.md
└── ...
```

## CI drift check

The GitHub Actions workflow runs `dotnet run --project tools/docs-gen -- --verify`
on every PR that touches `packages/Clustral.Sdk/Results/ResultErrors.cs`. If a
new factory is added without re-running the tool, the build fails.
