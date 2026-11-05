// Assembles the dashboard into a site folder: the page files from this folder, plus the
// pinned browser libraries from node_modules (installed and integrity-checked by `npm ci`).
//
//   npm ci && npm run build -- ../out/site
//
// Everything the page loads is then served from the site itself, so the Content Security
// Policy can stay 'self' and visitors' browsers never contact a third party (ADR 0008).
// That includes DuckDB's Parquet extension, which DuckDB-WASM would otherwise download from
// extensions.duckdb.org on first use. DuckDB checks the extension's signature when loading
// it, so a tampered file is refused.

import { copyFile, cp, mkdir, rm, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { build } from "esbuild";

const here = path.dirname(fileURLToPath(import.meta.url));
const require = createRequire(import.meta.url);
const outDir = path.resolve(process.argv[2] ?? path.join(here, "..", "out", "site"));
const vendor = path.join(outDir, "vendor");
const modules = path.join(here, "node_modules");
const duckdbDist = path.join(modules, "@duckdb", "duckdb-wasm", "dist");

// The engine runs in a Web Worker with the "eh" build, which every current browser supports.
const PLATFORM = "wasm_eh";

async function main() {
  // Page files. data/ is left alone: the pipeline writes it.
  await mkdir(outDir, { recursive: true });
  for (const file of ["index.html", "styles.css"]) {
    await copyFile(path.join(here, file), path.join(outDir, file));
  }
  await rm(path.join(outDir, "js"), { recursive: true, force: true });
  await cp(path.join(here, "js"), path.join(outDir, "js"), { recursive: true });
  await rm(vendor, { recursive: true, force: true });
  await mkdir(path.join(vendor, "duckdb"), { recursive: true });

  // Chart.js: one ready-made file.
  await copyFile(path.join(modules, "chart.js", "dist", "chart.umd.min.js"), path.join(vendor, "chart.umd.min.js"));

  // DuckDB-WASM's browser API, bundled with Apache Arrow, which it imports by package name.
  await build({
    stdin: { contents: 'export { AsyncDuckDB, ConsoleLogger, LogLevel } from "@duckdb/duckdb-wasm";', resolveDir: here },
    bundle: true,
    format: "esm",
    minify: true,
    legalComments: "eof",
    outfile: path.join(vendor, "duckdb", "duckdb.js"),
    logLevel: "warning",
  });
  for (const file of ["duckdb-eh.wasm", "duckdb-browser-eh.worker.js"]) {
    await copyFile(path.join(duckdbDist, file), path.join(vendor, "duckdb", file));
  }

  // The Parquet extension, at the path DuckDB asks for: {repository}/{version}/{platform}/.
  const version = await duckdbVersion();
  const name = "parquet.duckdb_extension.wasm";
  const url = `https://extensions.duckdb.org/${version}/${PLATFORM}/${name}`;
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`Downloading ${url} failed: HTTP ${response.status}`);
  }
  const extensionDir = path.join(vendor, "duckdb", "extensions", version, PLATFORM);
  await mkdir(extensionDir, { recursive: true });
  const bytes = Buffer.from(await response.arrayBuffer());
  await writeFile(path.join(extensionDir, name), bytes);

  console.log(`Dashboard built in ${outDir} (DuckDB ${version}, Parquet extension ${(bytes.length / 1e6).toFixed(1)} MB)`);
}

/** Asks the installed DuckDB-WASM build which DuckDB version it is, so the extension always matches. */
async function duckdbVersion() {
  const duckdb = require(path.join(duckdbDist, "duckdb-node-blocking.cjs"));
  const bundles = {
    mvp: { mainModule: path.join(duckdbDist, "duckdb-mvp.wasm"), mainWorker: "" },
    eh: { mainModule: path.join(duckdbDist, "duckdb-eh.wasm"), mainWorker: "" },
  };
  const db = await duckdb.createDuckDB(bundles, new duckdb.VoidLogger(), duckdb.NODE_RUNTIME);
  await db.instantiate();
  return db.getVersion();
}

await main();
