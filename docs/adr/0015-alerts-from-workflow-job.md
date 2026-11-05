# 0015: Raise and resolve alerts from a separate workflow job

**Status:** Accepted
**Date:** 2026-11-05

## Context

ADR 0011 chose GitHub Issues for alerting, with the pipeline itself opening an issue when a run fails or data is stale. Building it in M3 showed three problems with having the C# pipeline write issues:

- **Permissions:** the pipeline job would need `issues: write` for its whole run, although only the alert needs it (NFR-SEC-02).
- **Coverage:** failures before or after the C# program, such as a build error, a timeout, or a failed Pages deploy, would never be reported, because the program isn't running then (NFR-OBS-02).
- **Closing:** ADR 0011 had alerts closed by hand, so a fixed problem could leave a stale open alert and hide the next one.

## Decision

The pipeline only **reports** what happened, as step outputs: the failed step, a cleaned one-line error, and the data age when it's stale. A separate **alert** job, the only job with `issues: write`, runs after every pipeline run, whatever the outcome:

- Any failed or cancelled job comments on the open alert, or opens one if none is open (FR-OPS-02, FR-OPS-03).
- The next successful run comments that the problem is resolved and closes the alert.
- Stale data that a successful run has already fixed opens an alert and closes it at once, so the gap is on record.

Alerts use the label `pipeline-alert`, and at most one is open at a time. Values reach the alert script only through environment variables, so error text can't run as a command.

This supersedes ADR 0011, keeping its choice of GitHub Issues.

## Alternatives considered

- **Pipeline writes issues (ADR 0011 as written):** simpler wiring, but the broad permission and the unreported failures above.
- **A third-party alerting action:** less script to maintain, but another dependency with write access to issues, for logic that is about 50 lines of shell.
- **Email notifications from GitHub Actions:** built in, but they only reach the owner's inbox, leave no public record, and don't deduplicate.

## Consequences

- Only one short job holds `issues: write`, and it never runs code from the repository's C# projects.
- Build errors, timeouts, and deploy failures are reported the same way as pipeline failures.
- Alerts close themselves, so an open alert always means a current problem.
- The alert logic is shell, not C#, so it isn't covered by the unit tests; it was tested with a stand-in for the `gh` command, and is checked by hand the first time each kind of failure happens ([test strategy](../test-strategy.md)).
- If the workflow doesn't run at all, for example because GitHub has disabled the schedule, no alert is raised. That case is covered by the risk R6 review, not by this job.
