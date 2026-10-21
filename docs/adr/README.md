# Architecture Decision Records

This folder records the significant architecture decisions for the Feedback Intelligence Platform. Each record explains the context, the decision, the alternatives considered, and the consequences.

Accepted records are not edited. If a decision changes, a new record supersedes the old one, and the old one's status is updated to point to it.

| # | Decision | Status | Date |
|---|---|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions using ADRs | Accepted | 2026-10-14 |
| [0002](0002-run-on-github.md) | Run entirely on GitHub instead of a paid cloud platform | Accepted | 2026-10-14 |
| [0003](0003-use-csharp-dotnet.md) | Use C# and .NET 8 for the pipeline application | Superseded by 0014 | 2026-10-14 |
| [0004](0004-sql-server-express-container.md) | Use SQL Server Express in a temporary container on the runner | Accepted | 2026-10-14 |
| [0005](0005-history-parquet-data-branch.md) | Store history as Parquet files on a dedicated data branch | Accepted | 2026-10-14 |
| [0006](0006-github-models-ai.md) | Use GitHub Models for AI enrichment | Accepted | 2026-10-14 |
| [0007](0007-presidio-pii-redaction.md) | Use Microsoft Presidio for PII redaction | Accepted | 2026-10-14 |
| [0008](0008-static-dashboard-duckdb-wasm.md) | Serve a static dashboard queried in the browser with DuckDB-WASM | Accepted | 2026-10-14 |
| [0009](0009-raw-cleaned-reporting-layers.md) | Organize data in raw, cleaned, and reporting layers | Accepted | 2026-10-14 |
| [0010](0010-daily-batch-processing.md) | Process data in a daily batch rather than streaming | Accepted | 2026-10-14 |
| [0011](0011-github-issues-alerting.md) | Use GitHub Issues for alerting | Accepted | 2026-10-14 |
| [0012](0012-wsl2-docker-development.md) | Develop on Windows using WSL 2 and Docker Desktop | Accepted | 2026-10-14 |
| [0013](0013-exclude-bodies-from-history.md) | Exclude issue bodies from stored history | Accepted | 2026-10-14 |
| [0014](0014-target-dotnet-10.md) | Target .NET 10 instead of .NET 8 | Accepted | 2026-10-19 |

## Template

New records copy this structure and take the next number.

```markdown
# NNNN: Decision stated as a short sentence

**Status:** Proposed | Accepted | Superseded by NNNN
**Date:** YYYY-MM-DD

## Context
## Decision
## Alternatives considered
## Consequences
```
