# 0017: Pin the dashboard's libraries with npm and serve them from the site

**Status:** Accepted
**Date:** 2026-11-05

## Context

ADR 0008 chose a static dashboard that queries Parquet files in the browser with DuckDB-WASM, with charts from Chart.js. Building it in M3 raised three practical questions:

- **Where the libraries come from:** loading them from a public CDN would mean every visitor's browser contacts a third party, and the page's Content Security Policy would have to allow it.
- **The Parquet extension:** DuckDB-WASM doesn't read Parquet on its own. On first use it downloads a Parquet extension from extensions.duckdb.org, which raises the same third-party question.
- **Updates:** copying library files into the repository by hand leaves no record of their versions and gets no update or vulnerability alerts.

## Decision

The dashboard has its own `package.json` and `package-lock.json`. npm installs the exact library versions, checking each against the hash in the lock file, and Dependabot proposes updates. `dashboard/build.mjs` assembles the site:

- It copies the page files and the pinned libraries, and bundles DuckDB-WASM's browser code with esbuild.
- It downloads the Parquet extension for the exact DuckDB version installed, into the site itself. The page tells DuckDB to load extensions from there. DuckDB checks the extension's signature before loading it.

Everything the page loads is served from the site, so the Content Security Policy stays `'self'`. Node.js becomes a build tool for the dashboard; the pipeline stays in C#.

## Alternatives considered

- **Load from a CDN:** no build step, but a third-party request on every visit, a looser Content Security Policy, and a dependency on the CDN being up.
- **Commit the library files:** no build step, but a 34 MB WebAssembly file in the repository's history for every version, and no automatic updates.
- **Replace DuckDB-WASM with a small JavaScript Parquet reader (hyparquet):** about 47 KB instead of 34 MB, but no SQL in the browser. It was considered and declined; the trade-off and measurements are in [design section 6](../design.md#6-dashboard).

## Consequences

- Visitors' browsers contact only GitHub Pages.
- Library versions are visible in the lock file, updated by Dependabot, and audited in CI.
- Local development of the dashboard needs Node.js 22 or later, and the README says so.
- Building the site needs network access to npm and to extensions.duckdb.org. If the extension can't be downloaded, the build fails before anything is deployed.
- The dashboard's JavaScript has its own tests (`npm test`), which run the dashboard's real queries in the Node build of the same DuckDB package.
