# 0002: Run entirely on GitHub instead of a paid cloud platform

**Status:** Accepted
**Date:** 2026-10-14

## Context

The system must run daily and stay publicly viewable indefinitely at $0, with no trials, credits, or payment method on file. Cloud free offers (Azure's $200 credit, AWS's 6-month free plan) expire, and services like hosted databases bill hourly once credits run out. The project may need to stay live for months before being shown to anyone.

## Decision

Use GitHub as the production platform: **Actions** for scheduling and running the pipeline, **Pages** for hosting the dashboard, **Issues** for alerts, and the **repository** for code, config, and stored history.

## Alternatives considered

- **Azure (Functions, Azure SQL free offer):** a natural fit for a .NET and SQL Server stack, but requires a pay-as-you-go subscription with a card on file, and supporting resources such as storage accounts aren't covered by free grants.
- **AWS (Lambda, RDS):** Lambda is always free at this scale, but RDS for SQL Server bills hourly after free-plan credits, and the free plan ends after 6 months.
- **A home server:** free, but it depends on a personal computer being on, and it isn't publicly reachable without extra setup.

## Consequences

- Zero cost with no expiry date, so the live system runs indefinitely.
- No hands-on cloud platform operation; this is mitigated by keeping external services behind interfaces and adding Bicep templates validated in CI (M7) to show how it would deploy to Azure.
- Bound by GitHub's limits: runner time per job, rate limits, and scheduled workflows pausing after about 60 days of repository inactivity (risk R6).
- The repository must be public for Actions and Pages to remain free.
