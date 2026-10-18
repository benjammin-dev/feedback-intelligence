# 0005: Store history as Parquet files on a dedicated data branch

**Status:** Accepted
**Date:** 2026-10-14

## Context

The database is destroyed with the runner after every run (ADR 0004), but trends and caching depend on data from previous runs. History must be stored somewhere free, permanent, and readable by both the pipeline and the dashboard. Only redacted data may be kept (FR-PRV-03).

## Decision

At the end of each successful run, export the cleaned layer, AI results, and run metrics as **Parquet** files and commit them to a dedicated **`data` branch** in the repository. At the start of each run, restore from that branch.

## Alternatives considered

- **Commit to the main branch:** simplest, but daily data commits would bury code changes in the history and make the commit log hard to read.
- **GitHub Actions artifacts:** built for passing files between jobs, but they expire (90 days at most), so history would be lost.
- **GitHub Actions cache:** evicted after a period without use and not intended for durable storage.
- **GitHub Releases assets:** durable and free, and a good fallback if the repository grows too large (risk R9), but clumsier to update daily.
- **External cloud storage:** conflicts with ADR 0002.

## Consequences

- Parquet is compact and columnar, and DuckDB-WASM reads it directly, so one format serves both the pipeline and the dashboard.
- Code history on `main` stays clean; data history is versioned separately and can be pruned without touching code.
- Repository size must be monitored (risk R9), with a limited history window.
- Whether daily commits to a non-default branch count as "activity" for keeping scheduled workflows enabled (risk R6) must be verified; if not, another way to keep the schedule active is needed.
