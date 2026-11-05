# 0008: Serve a static dashboard queried in the browser with DuckDB-WASM

**Status:** Accepted
**Date:** 2026-10-14

## Context

The dashboard must be public, free, always available, and support filtering and search across the full dataset in under 2 seconds (NFR-PERF-01). There's no free server to run a backend on (ADR 0002), and only static files can be hosted on GitHub Pages.

## Decision

Build the dashboard as **static HTML and JavaScript** on GitHub Pages. The pipeline publishes reporting data as Parquet files, and the browser queries them with **DuckDB-WASM**, a full SQL engine that runs inside the web page. Charts are drawn with a lightweight JavaScript charting library.

## Alternatives considered

- **A server-backed web app (ASP.NET, Streamlit):** flexible, but needs a server running all the time, which isn't free (ADR 0002).
- **Power BI service:** a capable analytics tool, but publishing and sharing require a paid license. A Power BI Desktop report remains planned as a Could-have (US-20).
- **Published JSON plus hand-written JavaScript filtering:** smaller and simpler to load, but every new filter or aggregation needs custom code, and it grows awkward as queries get more complex.
- **Pre-rendered static charts:** fastest to load, but no interactive filtering, which fails US-06.

## Consequences

- No backend to run, secure, or pay for; the site stays up even when the pipeline fails (FR-OPS-05).
- Dashboard queries are written in SQL, the same language as the pipeline's transforms.
- DuckDB-WASM adds a few megabytes to the first page load. Meeting the 3-second target (NFR-PERF-02) may require loading it in the background after the first view appears, and must be measured.
- All published data is public by definition, so only fields in the data contract may be exported (NFR-PRIV-02).

## Measurements (2026-11-01)

Recorded when the dashboard was built, with DuckDB-WASM 1.32.0:

- The engine is a 34 MB download (7.7 MB if compressed in transit), and reading Parquet needs DuckDB's Parquet extension as well. At 10 Mbps, the Overview appears in 0.3 seconds because it doesn't wait for the engine; Trends and Issues are ready about 7 to 29 seconds after a first visit, depending on compression, and cached after that.
- Searches over 100,000 issues return in about 0.1 seconds, well under the 2-second target.
- A small JavaScript Parquet reader (hyparquet, about 47 KB) was considered at this point, since the data is small. DuckDB-WASM was kept for SQL queries in the browser; the engine is loaded only in the background, and is self-hosted with the Parquet extension so no third party is contacted.
