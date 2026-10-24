// Walking-skeleton dashboard: shows one number from the published data files.
// All data is inserted with textContent, never innerHTML, so issue data can't inject markup.
"use strict";

async function load(name) {
  const response = await fetch(`data/${name}`, { cache: "no-cache" });
  if (!response.ok) {
    throw new Error(`${name}: HTTP ${response.status}`);
  }
  return response.json();
}

function show(id, text) {
  document.getElementById(id).textContent = text;
}

async function main() {
  try {
    const [site, overview] = await Promise.all([load("site_info.json"), load("overview.json")]);
    const current = overview.find((row) => row.repository === "all" && row.period === "current");
    const previous = overview.find((row) => row.repository === "all" && row.period === "previous");

    show("new-issues", current.new_issues.toLocaleString());

    const change = current.new_issues - previous.new_issues;
    const sign = change > 0 ? "+" : "";
    show("change", `${sign}${change.toLocaleString()} compared with the 7 days before (${previous.new_issues.toLocaleString()})`);

    const repositories = JSON.parse(site.repositories).join(", ");
    show("updated", `Data as of ${new Date(site.data_updated_at).toLocaleString()} · ${repositories}`);
  } catch (error) {
    const element = document.getElementById("error");
    element.hidden = false;
    element.textContent = `The data couldn't be loaded (${error.message}).`;
  }
}

main();
