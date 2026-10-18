# 0001: Record architecture decisions using ADRs

**Status:** Accepted
**Date:** 2026-10-14

## Context

This project is built by one person over several months, with AI assistance, and will be reviewed by people who weren't part of the decisions. Reasoning that lives only in conversation is lost.

## Decision

Record each significant architecture decision as a numbered Markdown file in `docs/adr/`, using the Context / Decision / Alternatives / Consequences format.

## Alternatives considered

- **A single "decisions" section in `design.md`:** simpler, but it grows long, and it's hard to see when each decision was made or replaced.
- **No formal record:** fastest, but it leaves no way to explain trade-offs later.

## Consequences

- Every significant decision has a clear rationale that can be linked from code, pull requests, and the README.
- Writing ADRs takes a little time per decision.
- Accepted ADRs are not edited; changes require a new ADR that supersedes the old one.
