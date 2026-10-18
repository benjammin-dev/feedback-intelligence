# 0011: Use GitHub Issues for alerting

**Status:** Accepted
**Date:** 2026-10-14

## Context

Failures and stale data must be reported automatically (FR-OPS-02), with no silent failures. The operator needs to see what broke, where to find the logs, and a record of past incidents. Any alerting service must be free and not require a separate account (ADR 0002).

## Decision

When a run fails or data is older than 48 hours, the pipeline **opens a GitHub issue** with an `alert` label, containing the failed step, error message, and a link to the run logs. If an open alert for the same problem already exists, it **adds a comment** instead of opening a duplicate (FR-OPS-03). Alerts are closed manually once the problem is fixed, ideally linked to the fixing pull request.

## Alternatives considered

- **GitHub Actions' built-in failure emails:** automatic and free, but they only cover failed workflows, not stale data or quality failures, and they leave no shared incident record.
- **Chat notifications (Slack, Discord, Teams webhooks):** immediate, but they need a separate workspace and a stored webhook secret, and messages aren't easy to track to resolution.
- **A monitoring service (Azure Monitor, PagerDuty):** industry-standard paging, but requires paid accounts.

## Consequences

- Alerts live next to the code, can be linked to fixes and postmortems, and form a visible incident history.
- GitHub notifies the repository owner of new issues by email, so alerts still reach the operator without extra setup.
- The workflow needs permission to write issues, granted only to the alerting step (NFR-SEC-02).
- There's no paging or escalation. That's acceptable for a daily batch system with a 48-hour freshness limit.
- Alerts can only fire when the pipeline runs. If the schedule itself is disabled (risk R6), no alert is raised, so the schedule must be checked periodically.
