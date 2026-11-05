// The dashboard: three pages in one document, switched by the address's #fragment.
// Every piece of published data is inserted with textContent, never innerHTML, so issue text
// can't inject markup, and only GitHub issue links are made clickable.

import {
  ALL_REPOSITORIES,
  buildChoicesQuery,
  buildIssueQueries,
  buildWeeklyQuery,
  describeChange,
  formatDate,
  formatDateTime,
  formatNumber,
  isGitHubIssueUrl,
  isStale,
  overviewFor,
  PAGE_SIZE,
  SORTS,
  timeAgo,
} from "./logic.js";

const PAGES = ["overview", "trends", "issues"];
const REPOSITORY_KEY = "fip.repository";

const state = {
  site: null,
  overview: [],
  repository: ALL_REPOSITORIES,
  issueOffset: 0,
  issueRequest: 0,
};

const $ = (selector) => document.querySelector(selector);

// The query engine and the chart library are loaded only when first needed, so the Overview
// appears without waiting for them (NFR-PERF-02).
let database = null;
let charting = null;

function loadDatabase() {
  database ??= import("./db.js").then(async (db) => ({ connection: await db.connect(), run: db.run }));
  database.catch(() => {
    database = null; // let a later visit try again
  });
  return database;
}

function loadCharts() {
  charting ??= new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = "vendor/chart.umd.min.js";
    script.onload = resolve;
    script.onerror = () => reject(new Error("vendor/chart.umd.min.js couldn't be loaded"));
    document.head.append(script);
  }).then(() => import("./charts.js"));
  charting.catch(() => {
    charting = null;
  });
  return charting;
}

function element(tag, text, className) {
  const node = document.createElement(tag);
  if (text != null) {
    node.textContent = text;
  }
  if (className) {
    node.className = className;
  }
  return node;
}

function showError(message) {
  const box = $("#error");
  box.textContent = message;
  box.hidden = false;
}

// ---------------------------------------------------------------------------------------
// Start-up: the two small JSON files are all the Overview needs.
// ---------------------------------------------------------------------------------------

async function loadJson(name) {
  const response = await fetch(`data/${name}`, { cache: "no-cache" });
  if (!response.ok) {
    throw new Error(`${name}: HTTP ${response.status}`);
  }
  return response.json();
}

async function start() {
  try {
    [state.site, state.overview] = await Promise.all([loadJson("site_info.json"), loadJson("overview.json")]);
  } catch (error) {
    showError(`The dashboard data couldn't be loaded (${error.message}). Please try again later.`);
    return;
  }

  showUpdated();
  setUpRepositoryPicker();
  setUpTrends();
  setUpIssues();
  window.addEventListener("hashchange", () => showPage(true));
  showPage(false);

  // Warm up the query engine for the other pages, unless the visitor asked to save data.
  if (!navigator.connection?.saveData) {
    (window.requestIdleCallback ?? setTimeout)(() => loadDatabase().catch(() => {}));
  }
}

/** When the data was last updated (FR-DSH-05), and a warning if that's over 48 hours ago. */
function showUpdated() {
  const updated = new Date(state.site.data_updated_at);
  const now = new Date();
  $("#updated").textContent = `Data updated ${formatDateTime(updated)} (${timeAgo(updated, now)})`;

  if (isStale(updated, now)) {
    const notice = $("#stale");
    notice.replaceChildren(
      "This data is more than two days old, so the daily update may be failing. ",
      link(
        "https://github.com/benjammin-dev/feedback-intelligence/issues?q=label%3Apipeline-alert",
        "See pipeline alerts",
      ),
      ".",
    );
    notice.hidden = false;
  }
}

function setUpRepositoryPicker() {
  const picker = $("#repository");
  const repositories = JSON.parse(state.site.repositories ?? "[]");
  for (const repository of repositories) {
    picker.append(new Option(repository, repository));
  }

  let saved = null;
  try {
    saved = localStorage.getItem(REPOSITORY_KEY);
  } catch {
    // Storage can be unavailable (private windows); the default is fine.
  }
  if (saved && repositories.includes(saved)) {
    picker.value = saved;
    state.repository = saved;
  }

  picker.addEventListener("change", () => {
    state.repository = picker.value;
    try {
      localStorage.setItem(REPOSITORY_KEY, picker.value);
    } catch {
      // Not remembered, but still applied.
    }
    renderCurrentPage();
  });
}

// ---------------------------------------------------------------------------------------
// Pages
// ---------------------------------------------------------------------------------------

function currentPage() {
  const page = location.hash.slice(1);
  return PAGES.includes(page) ? page : "overview";
}

function showPage(moveFocus) {
  const page = currentPage();
  for (const section of document.querySelectorAll("[data-page]")) {
    section.hidden = section.dataset.page !== page;
  }
  for (const link of document.querySelectorAll("[data-page-link]")) {
    if (link.dataset.pageLink === page) {
      link.setAttribute("aria-current", "page");
    } else {
      link.removeAttribute("aria-current");
    }
  }
  document.title = `${page[0].toUpperCase()}${page.slice(1)} · Feedback Intelligence`;
  if (moveFocus) {
    $(`#${page}-title`).focus();
  }
  renderCurrentPage();
}

function renderCurrentPage() {
  const page = currentPage();
  if (page === "overview") {
    renderOverview();
  } else if (page === "trends") {
    renderTrends();
  } else {
    state.issueOffset = 0;
    renderIssues();
  }
}

// --- Overview (US-01) --------------------------------------------------------------------

const OVERVIEW_FIGURES = [
  { field: "new_issues", label: "New issues" },
  { field: "closed_issues", label: "Closed issues" },
  { field: "bug_percent", label: "Bugs, as a share of new issues", unit: "%", ai: true },
  { field: "critical_count", label: "Critical issues", ai: true },
  {
    field: "median_days_to_close",
    label: "Median days to close",
    unit: " days",
    note: "For issues closed in the period",
  },
];

function renderOverview() {
  const { current, previous } = overviewFor(state.overview, state.repository);
  const cards = $("#overview-cards");
  cards.replaceChildren();

  if (!current) {
    $("#overview-period").textContent = "No figures are available for this repository yet.";
    return;
  }

  const start = new Date(current.period_start);
  const end = new Date(current.period_end);
  $("#overview-period").textContent =
    `The last 7 days (${formatDate(start)} to ${formatDate(end)}), compared with the 7 days before.`;

  for (const figure of OVERVIEW_FIGURES) {
    const value = current[figure.field];
    const card = element("article", null, "card");
    card.append(element("h2", figure.label));
    card.append(
      element("p", value == null ? "—" : `${formatNumber(value)}${figure.unit === "%" ? "%" : ""}`, "figure"),
    );

    const change = describeChange(value, previous?.[figure.field], figure.unit === "%" ? " points" : figure.unit);
    if (change) {
      card.append(element("p", change.text, `change ${change.direction}`));
    } else if (value == null && figure.ai) {
      card.append(element("p", "Available once AI classification is added.", "muted"));
    } else if (value == null) {
      card.append(element("p", "Nothing was closed in this period.", "muted"));
    }
    if (figure.note) {
      card.append(element("p", figure.note, "muted small"));
    }
    cards.append(card);
  }
}

// --- Trends (US-02) ----------------------------------------------------------------------

function setUpTrends() {
  const form = $("#trend-filters");
  form.addEventListener("change", () => renderTrends());
  form.addEventListener("reset", () => setTimeout(() => renderTrends()));
}

async function renderTrends() {
  const status = $("#trends-status");
  const form = $("#trend-filters");
  status.textContent = "Loading the query engine. This can take a few seconds on the first visit.";

  let rows;
  let charts;
  try {
    const [{ connection, run }, chartModule] = await Promise.all([loadDatabase(), loadCharts()]);
    rows = await run(connection, buildWeeklyQuery(state.repository, form.from.value, form.to.value));
    charts = chartModule;
  } catch (error) {
    status.textContent = "";
    showError(`The trends couldn't be loaded (${error.message}).`);
    return;
  }

  if (rows.length === 0) {
    status.textContent = "No complete weeks match these dates.";
  } else {
    const first = formatDate(new Date(rows[0].week_ms));
    const last = formatDate(new Date(rows[rows.length - 1].week_ms));
    status.textContent = `${rows.length} complete weeks, from the week of ${first} to the week of ${last}. The current week is left out until it ends.`;
  }

  const volume = $("#chart-volume");
  const median = $("#chart-median");
  charts.drawVolumeChart(volume, rows);
  charts.drawMedianChart(median, rows);
  volume.setAttribute("aria-label", chartSummary(rows, "new and closed issues per week"));
  median.setAttribute("aria-label", chartSummary(rows, "median days to close per week"));

  const body = $("#trends-table tbody");
  body.replaceChildren(
    ...rows.map((row) => {
      const tr = document.createElement("tr");
      tr.append(
        element("td", formatDate(new Date(row.week_ms))),
        element("td", formatNumber(row.new_issues)),
        element("td", formatNumber(row.closed_issues)),
        element("td", formatNumber(row.median_days_to_close)),
      );
      return tr;
    }),
  );
}

function chartSummary(rows, what) {
  if (rows.length === 0) {
    return `Chart of ${what}: no data.`;
  }
  return `Chart of ${what}, ${rows.length} weeks. The numbers are in the table below the charts.`;
}

// --- Issue explorer (US-06, US-08) ------------------------------------------------------

function setUpIssues() {
  const form = $("#issue-filters");
  for (const [key, sort] of Object.entries(SORTS)) {
    form.sort.append(new Option(sort.label, key));
  }

  let typing;
  form.addEventListener("input", (event) => {
    clearTimeout(typing);
    const delay = event.target.name === "search" ? 250 : 0;
    typing = setTimeout(() => {
      state.issueOffset = 0;
      renderIssues();
    }, delay);
  });
  form.addEventListener("submit", (event) => event.preventDefault());
  form.addEventListener("reset", () =>
    setTimeout(() => {
      state.issueOffset = 0;
      renderIssues();
    }),
  );

  $("#more").addEventListener("click", () => {
    state.issueOffset += PAGE_SIZE;
    renderIssues();
  });
}

function issueFilters() {
  const form = $("#issue-filters");
  return {
    repository: state.repository,
    search: form.search.value,
    state: form.state.value,
    category: form.category.value,
    severity: form.severity.value,
    from: form.from.value,
    to: form.to.value,
  };
}

async function renderIssues() {
  const request = ++state.issueRequest;
  const count = $("#issue-count");
  const appending = state.issueOffset > 0;
  if (!appending) {
    count.textContent = "Loading the query engine. This can take a few seconds on the first visit.";
  }

  let total;
  let rows;
  try {
    const { connection, run } = await loadDatabase();
    await fillChoices(connection, run);
    const started = performance.now();
    const queries = buildIssueQueries(issueFilters(), $("#issue-filters").sort.value, state.issueOffset);
    [[{ total }], rows] = await Promise.all([run(connection, queries.count), run(connection, queries.page)]);
    if (request !== state.issueRequest) {
      return; // a newer search started while this one ran
    }
    const seconds = (performance.now() - started) / 1000;
    const shown = state.issueOffset + rows.length;
    count.textContent =
      total === 0
        ? "No issues match these filters."
        : `Showing ${formatNumber(shown)} of ${formatNumber(total)} issues (found in ${seconds.toFixed(2)} seconds)`;
    $("#more").hidden = shown >= total;
  } catch (error) {
    count.textContent = "";
    showError(`The issues couldn't be loaded (${error.message}).`);
    return;
  }

  const body = $("#issue-table tbody");
  const newRows = rows.map(issueRow);
  if (appending) {
    body.append(...newRows);
  } else {
    body.replaceChildren(...newRows);
  }
}

let choicesFilled = false;

/** Fills the category and severity filters from the data, or explains why they're empty. */
async function fillChoices(connection, run) {
  if (choicesFilled) {
    return;
  }
  choicesFilled = true;
  const form = $("#issue-filters");
  for (const field of ["category", "severity"]) {
    const values = (await run(connection, buildChoicesQuery(field))).map((row) => row.value);
    const select = form[field];
    for (const value of values) {
      select.append(new Option(label(value), value));
    }
    if (values.length === 0) {
      select.disabled = true;
      select.title = "Available once AI classification is added";
    }
  }
}

function issueRow(issue) {
  const tr = document.createElement("tr");

  const number = document.createElement("td");
  number.dataset.label = "Issue";
  number.append(
    isGitHubIssueUrl(issue.html_url)
      ? link(issue.html_url, `#${issue.issue_number}`, `Issue ${issue.issue_number} on GitHub (opens in a new tab)`)
      : `#${issue.issue_number}`,
  );

  const closed = issue.state === "closed";
  const stateText = closed ? `Closed${issue.state_reason ? ` (${label(issue.state_reason)})` : ""}` : "Open";

  tr.append(
    number,
    cell("Title", issue.title, "title"),
    cell("Repository", issue.repository.split("/")[1] ?? issue.repository),
    cell("State", stateText),
    cell("Created", formatDate(new Date(issue.created_ms))),
    cell("Days to close", issue.days_to_close == null ? "—" : formatNumber(issue.days_to_close)),
    aiCell("Category", issue.category, issue.ai_status),
    aiCell("Severity", issue.severity, issue.ai_status),
  );
  return tr;
}

function cell(name, text, className) {
  const td = element("td", text, className);
  td.dataset.label = name;
  return td;
}

/** AI fields say why they're empty, and are marked as AI-generated once filled (FR-DSH-06). */
function aiCell(name, value, aiStatus) {
  if (value != null) {
    const td = cell(name, null);
    const content = element("span", label(value));
    content.append(element("span", "AI", "ai-badge"));
    td.append(content);
    td.title = "AI-generated";
    return td;
  }
  const td = cell(name, aiStatus === "unclassified" ? "Unclassified" : "Pending", "muted");
  td.title = aiStatus === "unclassified" ? "The AI couldn't classify this issue" : "AI classification hasn't run yet";
  return td;
}

function link(url, text, accessibleName) {
  const a = element("a", text);
  a.href = url;
  a.target = "_blank";
  a.rel = "noopener noreferrer";
  if (accessibleName) {
    a.setAttribute("aria-label", accessibleName);
  }
  return a;
}

/** Turns stored values such as feature_request into "Feature request". */
function label(value) {
  const text = String(value).replaceAll("_", " ");
  return text[0].toUpperCase() + text.slice(1);
}

start();
