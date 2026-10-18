# 0004: Use SQL Server Express in a temporary container on the runner

**Status:** Accepted
**Date:** 2026-10-14

## Context

The pipeline needs a relational database for layered transforms, upserts, and quality checks. SQL Server offers mature T-SQL support for these (MERGE, window functions, partitioning) and strong .NET integration. There's no free, permanent place to host a database server, and each GitHub Actions runner is destroyed after the job.

## Decision

Start **SQL Server Express** in a Docker container at the beginning of each run, rebuild its state from versioned migrations and stored history, run all transforms, then discard it with the runner. Use **SQL Server Developer** edition only for local development.

## Alternatives considered

- **A hosted database (Azure SQL, Amazon RDS):** persistent and realistic, but conflicts with ADR 0002's $0 and no-card requirement.
- **SQLite or DuckDB:** simpler and lighter, but with less capable support for stored procedures, partitioning, and the layered transforms planned here.
- **SQL Server Developer in the pipeline:** free with all features, but its license prohibits production use.

## Consequences

- Full SQL Server and T-SQL in production at no cost, with correct licensing.
- Every run starts from a clean, reproducible state, which avoids drift between runs.
- Raw, unredacted text is destroyed with the runner automatically, which supports FR-PRV-03.
- Each run pays a startup cost (container start and history restore), covered by the 30-minute run target (NFR-PERF-03).
- Express limits (10 GB per database, limited memory and CPU) are acceptable given the 2 GB data target (NFR-SCL-03).
- State must be saved and restored explicitly between runs (see ADR 0005).
