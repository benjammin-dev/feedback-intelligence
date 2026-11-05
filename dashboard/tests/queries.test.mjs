// Runs the dashboard's real queries in DuckDB (the Node build of the same DuckDB-WASM
// package the browser uses) against tables shaped like the published Parquet files.
// Needs `npm ci` first.
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import path from "node:path";
import { after, before, test } from "node:test";
import { fileURLToPath } from "node:url";

import { buildChoicesQuery, buildIssueQueries, buildWeeklyQuery } from "../js/logic.js";

const require = createRequire(import.meta.url);
const dist = path.join(
  path.dirname(fileURLToPath(import.meta.url)),
  "..",
  "node_modules",
  "@duckdb",
  "duckdb-wasm",
  "dist",
);
const duckdb = require(path.join(dist, "duckdb-node-blocking.cjs"));

let db;
let conn;

before(async () => {
  db = await duckdb.createDuckDB(
    {
      mvp: { mainModule: path.join(dist, "duckdb-mvp.wasm"), mainWorker: "" },
      eh: { mainModule: path.join(dist, "duckdb-eh.wasm"), mainWorker: "" },
    },
    new duckdb.VoidLogger(),
    duckdb.NODE_RUNTIME,
  );
  await db.instantiate();
  conn = db.connect();

  // The same column types the pipeline publishes (data dictionary, reporting.issue and weekly_metrics).
  conn.query(`CREATE TABLE issues AS SELECT * FROM (VALUES
    ('microsoft/winget-cli', 1, 'Upgrade fails with 0x8a15000f', 'open', NULL,
      TIMESTAMP '2026-10-01 00:00:00', TIMESTAMP '2026-10-02 00:00:00', NULL, NULL::DECIMAL(7,1),
      'bug', 'high', NULL, NULL, 'classified', 'https://github.com/microsoft/winget-cli/issues/1'),
    ('microsoft/winget-cli', 2, 'Add an export command', 'closed', 'completed',
      TIMESTAMP '2026-10-07 23:59:59', TIMESTAMP '2026-10-20 00:00:00', TIMESTAMP '2026-10-12 11:59:59', 4.5::DECIMAL(7,1),
      'feature_request', NULL, NULL, 'Asks for a way to export the installed list', 'classified', 'https://github.com/microsoft/winget-cli/issues/2'),
    ('microsoft/PowerToys', 3, 'Upgrade crash on start', 'closed', 'completed',
      TIMESTAMP '2026-10-08 00:00:00', TIMESTAMP '2026-10-09 00:00:00', TIMESTAMP '2026-10-18 00:00:00', 10.0::DECIMAL(7,1),
      NULL, NULL, NULL, NULL, 'pending', 'https://github.com/microsoft/PowerToys/issues/3')
    ) AS t(repository, issue_number, title, state, state_reason, created_at, updated_at, closed_at, days_to_close,
           category, severity, severity_reason, summary, ai_status, html_url)`);
  conn.query(`CREATE TABLE weekly AS SELECT * FROM (VALUES
    ('all', TIMESTAMP '2026-09-28 00:00:00', 5, 2, NULL::INTEGER, 3.0::DECIMAL(7,1)),
    ('all', TIMESTAMP '2026-10-05 00:00:00', 7, 4, NULL::INTEGER, NULL::DECIMAL(7,1)),
    ('microsoft/winget-cli', TIMESTAMP '2026-10-05 00:00:00', 6, 3, NULL::INTEGER, 2.5::DECIMAL(7,1))
    ) AS t(repository, week_start, new_issues, closed_issues, critical_count, median_days_to_close)`);
});

after(() => {
  conn?.close();
});

/** Runs a built query the way the dashboard does: a prepared statement with parameters. */
function run({ sql, params }) {
  const statement = conn.prepare(sql);
  try {
    return statement
      .query(...params)
      .toArray()
      .map((row) => row.toJSON());
  } finally {
    statement.close();
  }
}

const issueNumbers = (filters, sort) => run(buildIssueQueries(filters, sort).page).map((row) => row.issue_number);
const total = (filters) => run(buildIssueQueries(filters).count)[0].total;

test("no filters returns every issue, newest first", () => {
  assert.deepEqual(issueNumbers({}), [3, 2, 1]);
  assert.equal(total({}), 3);
});

test("filters combine in any order", () => {
  assert.deepEqual(issueNumbers({ repository: "microsoft/winget-cli" }), [2, 1]);
  assert.deepEqual(issueNumbers({ state: "closed", repository: "microsoft/winget-cli" }), [2]);
  assert.deepEqual(issueNumbers({ category: "bug", severity: "high" }), [1]);
  assert.equal(total({ category: "docs" }), 0);
});

test("the date range is by creation date and includes both days", () => {
  assert.deepEqual(issueNumbers({ from: "2026-10-01", to: "2026-10-07" }), [2, 1]);
  assert.deepEqual(issueNumbers({ from: "2026-10-08" }), [3]);
});

test("search matches every word in the title or summary, ignoring case", () => {
  assert.deepEqual(issueNumbers({ search: "UPGRADE" }), [3, 1]);
  assert.deepEqual(issueNumbers({ search: "upgrade crash" }), [3]);
  assert.deepEqual(issueNumbers({ search: "installed list" }), [2]);
  assert.deepEqual(issueNumbers({ search: "%" }), []); // no wildcards
  assert.deepEqual(issueNumbers({ search: "x'); DROP TABLE issues; --" }), []);
  assert.equal(total({}), 3); // and the table is still there
});

test("sorting puts issues that aren't closed last for 'longest to close'", () => {
  assert.deepEqual(issueNumbers({}, "slowest"), [3, 2, 1]);
  assert.deepEqual(issueNumbers({}, "oldest"), [1, 2, 3]);
  assert.deepEqual(issueNumbers({}, "updated"), [2, 3, 1]);
});

test("rows come back as plain numbers and strings the page can show", () => {
  const [row] = run(buildIssueQueries({ search: "export" }).page);

  assert.equal(row.created_ms, Date.UTC(2026, 9, 7, 23, 59, 59));
  assert.equal(row.closed_ms, Date.UTC(2026, 9, 12, 11, 59, 59));
  assert.equal(row.days_to_close, 4.5);
  assert.equal(row.title, "Add an export command");
  assert.equal(run(buildIssueQueries({ search: "upgrade fails" }).page)[0].closed_ms, null);
});

test("filter choices list each value once", () => {
  assert.deepEqual(
    run(buildChoicesQuery("category")).map((row) => row.value),
    ["bug", "feature_request"],
  );
  assert.deepEqual(
    run(buildChoicesQuery("severity")).map((row) => row.value),
    ["high"],
  );
});

test("weekly rows for one repository, oldest first, inside the range", () => {
  const all = run(buildWeeklyQuery("all", "", ""));
  assert.deepEqual(
    all.map((row) => row.week_ms),
    [Date.UTC(2026, 8, 28), Date.UTC(2026, 9, 5)],
  );
  assert.deepEqual(
    all.map((row) => row.median_days_to_close),
    [3, null],
  );
  assert.equal(run(buildWeeklyQuery("all", "2026-10-01", "")).length, 1);
  assert.equal(run(buildWeeklyQuery("microsoft/winget-cli", "", ""))[0].new_issues, 6);
});
