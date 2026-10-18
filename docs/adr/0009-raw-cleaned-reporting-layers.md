# 0009: Organize data in raw, cleaned, and reporting layers

**Status:** Accepted
**Date:** 2026-10-14

## Context

Data passes through several stages: as fetched from GitHub, after redaction and cleanup, after AI enrichment, and as aggregated for the dashboard. Each stage has different rules about privacy, quality, and who may read it. Mixing them risks raw text leaking into published output and makes each step hard to test.

## Decision

Store data in three SQL Server layers, each in its own schema:

- **Raw:** issues exactly as fetched, including unredacted text. Exists only during a run; never saved or published.
- **Cleaned:** redacted, normalized, deduplicated, and typed issues, plus AI results keyed by issue and content hash. This layer is saved as history.
- **Reporting:** aggregates, metrics, topics, and views shaped for the dashboard. This is the only layer that gets published.

Each pipeline step reads from one layer and writes to the next. Steps don't pass data to each other directly.

## Alternatives considered

- **A single table transformed in place:** fewer objects, but no clear point where data becomes safe to keep or publish, and steps can't be tested separately.
- **Two layers, with aggregation in the browser:** less SQL, but slower dashboard queries and business logic split between SQL and JavaScript.
- **Transforming in C# instead of SQL:** familiar, but set-based work like aggregation and upserts is clearer and faster in T-SQL.

## Consequences

- Privacy boundaries are structural: AI and publishing steps can only read layers that contain redacted data.
- Each layer and each transform can be tested on its own with expected inputs and outputs (NFR-MNT-02).
- A failed step can be rerun from its input layer without repeating earlier steps.
- Some data is stored more than once across layers, which is acceptable at this volume (NFR-SCL-03).
- This follows the widely used layered (bronze, silver, gold) pattern from data engineering.
