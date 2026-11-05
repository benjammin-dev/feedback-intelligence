// The query engine: DuckDB-WASM, running in a Web Worker, with the published Parquet files
// loaded into in-memory tables (ADR 0008). It's started only when a page needs it, or in the
// background after the Overview appears, so the first page never waits for it (NFR-PERF-02).

import { AsyncDuckDB, ConsoleLogger, LogLevel } from "../vendor/duckdb/duckdb.js";

/** Each table and the published file it's loaded from. */
const TABLES = [
  { table: "issues", file: "issues.parquet" },
  { table: "weekly", file: "weekly_metrics.parquet" },
];

/** Starts the engine and loads the data. app.js calls this once and shares the connection. */
export function connect() {
  return open();
}

/** Runs a query built in logic.js as a prepared statement, and returns plain objects. */
export async function run(connection, { sql, params }) {
  const statement = await connection.prepare(sql);
  try {
    const result = await statement.query(...params);
    return result.toArray().map((row) => row.toJSON());
  } finally {
    await statement.close();
  }
}

async function open() {
  const vendor = new URL("vendor/duckdb/", document.baseURI).href;
  const worker = new Worker(`${vendor}duckdb-browser-eh.worker.js`);
  const db = new AsyncDuckDB(new ConsoleLogger(LogLevel.WARNING), worker);
  await db.instantiate(`${vendor}duckdb-eh.wasm`);

  const connection = await db.connect();

  // Load the Parquet extension from this site rather than extensions.duckdb.org (see build.mjs).
  // The first setting covers explicit installs, the second the automatic load on first use.
  const repository = `${vendor}extensions`.replaceAll("'", "''");
  await connection.query(`SET custom_extension_repository = '${repository}'`);
  await connection.query(`SET autoinstall_extension_repository = '${repository}'`);

  // Fetching the files whole is simpler than DuckDB's ranged HTTP reads and works with any web
  // server; they're small.
  await Promise.all(TABLES.map(({ file }) => loadFile(db, file)));
  for (const { table, file } of TABLES) {
    await connection.query(`CREATE OR REPLACE TABLE ${table} AS SELECT * FROM read_parquet('${file}')`);
  }

  return connection;
}

async function loadFile(db, file) {
  const response = await fetch(`data/${file}`, { cache: "no-cache" });
  if (!response.ok) {
    throw new Error(`${file}: HTTP ${response.status}`);
  }
  await db.registerFileBuffer(file, new Uint8Array(await response.arrayBuffer()));
}
