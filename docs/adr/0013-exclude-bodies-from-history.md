# 0013: Exclude issue bodies from stored history

**Status:** Accepted
**Date:** 2026-10-14

## Context

ADR 0005 stores history on a `data` branch in the repository. Because the repository must be public for GitHub Actions and Pages to remain free (ADR 0002), that branch is publicly readable. The original data design saved up to several thousand characters of each issue's redacted body in history, which would effectively publish issue text even though the dashboard deliberately shows only titles and AI summaries. Redaction reduces risk but isn't perfect (ADR 0007), so the less text kept, the less any missed personal information is exposed.

## Decision

Keep issue bodies only for the duration of the run that fetched them. The redacted body is used as AI input, then dropped: the `body_redacted` column is excluded from the history files. When older issues need reprocessing, for example after a prompt improvement, a backfill re-fetches them from GitHub and redacts them again.

## Alternatives considered

- **Keep redacted bodies in history:** simplest, and allows reprocessing without re-fetching, but publishes thousands of characters of user-written text per issue on a public branch.
- **Encrypt history with a repository secret:** keeps bodies private, but adds a long-lived secret to manage and rotate (NFR-SEC-01 prefers only the built-in token), and encrypted files can't be read by the dashboard or inspected easily.
- **Make the repository private:** removes public access, but GitHub Pages and Actions would no longer be free (ADR 0002).

## Consequences

- No issue body text exists anywhere outside a running pipeline, which closes the gap between the history branch and the "titles and summaries only" publishing rule.
- History is much smaller, which helps the storage target (NFR-SCL-03) and repository growth (risk R9).
- The AI step can only process issues fetched in the current run. This matches normal operation, since new or changed issues are always fetched, but reprocessing old issues requires a backfill.
- `body_length` is kept in history so the summary rule (FR-AI-02) still works without the body.
- The data contract lists `body_redacted` as excluded from history, and the automated contract test enforces it.
