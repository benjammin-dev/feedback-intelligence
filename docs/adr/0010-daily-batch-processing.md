# 0010: Process data in a daily batch rather than streaming

**Status:** Accepted
**Date:** 2026-10-14

## Context

Users want to know what's happening each week, not each minute. Data must never be more than 48 hours old (NFR-REL-02). Each run costs runner time, GitHub API calls, AI requests, and a history commit.

## Decision

Run the full pipeline **once a day at 06:00 UTC** on a schedule, plus manual runs on demand (FR-OPS-01). Each run processes only issues created or changed since the last successful run.

## Alternatives considered

- **Hourly runs:** fresher data, but 24 times the runner time and history commits, faster repository growth (risk R9), and AI requests spread too thin to batch efficiently.
- **Event-driven processing with webhooks:** near real time, but webhooks require admin access to the source repositories and a server to receive them, and we have neither.
- **Streaming:** suited to high-volume, low-latency needs, which this system doesn't have, at far greater complexity.

## Consequences

- Simple to build, test, and reason about: one run, one result, one commit per day.
- Data can be up to about 24 hours behind, well within the 48-hour limit.
- A failed run is recovered by the next scheduled run or a manual run, since fetching resumes from the last successful run (FR-ING-02).
- Daily batches give the AI step enough issues to group into efficient requests.
