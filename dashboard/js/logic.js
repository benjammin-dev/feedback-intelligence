// Pure functions behind the dashboard: no DOM and no network, so Node's built-in test
// runner can check them (dashboard/tests). Dates are compared and shown in UTC, the time
// zone every timestamp in the published data uses.

const DAY_MS = 24 * 60 * 60 * 1000;

/** The 'all' row covers every repository; each repository also has its own rows. */
export const ALL_REPOSITORIES = "all";

/**
 * Describes how a number changed between two periods, in words and an arrow, so the
 * meaning never depends on color (NFR-USE-02). Returns null when either value is missing.
 */
export function describeChange(current, previous, unit = "") {
  if (current == null || previous == null) {
    return null;
  }

  const difference = round1(current - previous);
  const from = `${formatNumber(previous)}${unit}`;
  if (difference === 0) {
    return { direction: "same", text: `No change from the 7 days before (${from})` };
  }

  const direction = difference > 0 ? "up" : "down";
  const arrow = direction === "up" ? "▲" : "▼";
  const word = direction === "up" ? "Up" : "Down";
  return {
    direction,
    text: `${arrow} ${word} ${formatNumber(Math.abs(difference))}${unit} from the 7 days before (${from})`,
  };
}

/** The overview figures for one repository (or 'all'), with the share of bugs worked out. */
export function overviewFor(rows, repository) {
  const pick = (period) => rows.find((row) => row.repository === repository && row.period === period) ?? null;
  const withBugShare = (row) =>
    row && {
      ...row,
      bug_percent:
        row.bug_count == null || row.new_issues === 0 ? null : round1((100 * row.bug_count) / row.new_issues),
    };
  return { current: withBugShare(pick("current")), previous: withBugShare(pick("previous")) };
}

/** Parses a yyyy-mm-dd input value as the start of that day in UTC, or null if empty or invalid. */
export function parseDay(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value ?? "")) {
    return null;
  }
  const date = new Date(`${value}T00:00:00Z`);
  return Number.isNaN(date.getTime()) ? null : date;
}

/** Formats a date as yyyy-mm-dd in UTC, the form date inputs use. */
export function toDayValue(date) {
  return date.toISOString().slice(0, 10);
}

/**
 * The issue columns the explorer shows. Times become milliseconds, and decimals and whole
 * numbers become plain JavaScript numbers (DuckDB would otherwise return BigInt or decimal objects).
 */
const ISSUE_COLUMNS = `repository, CAST(issue_number AS INTEGER) AS issue_number, title, state, state_reason,
  CAST(epoch_ms(created_at) AS DOUBLE) AS created_ms,
  CAST(epoch_ms(updated_at) AS DOUBLE) AS updated_ms,
  CAST(epoch_ms(closed_at) AS DOUBLE) AS closed_ms,
  CAST(days_to_close AS DOUBLE) AS days_to_close,
  category, severity, summary, ai_status, html_url`;

/** The sort orders the issue explorer offers. Only these fixed strings ever reach the SQL. */
export const SORTS = {
  newest: { label: "Newest first", orderBy: "created_at DESC, issue_number DESC" },
  oldest: { label: "Oldest first", orderBy: "created_at, issue_number" },
  updated: { label: "Recently updated", orderBy: "updated_at DESC, issue_number DESC" },
  slowest: { label: "Longest to close", orderBy: "days_to_close DESC NULLS LAST, issue_number DESC" },
};

/** The most rows one query returns; more are fetched with "Show more". */
export const PAGE_SIZE = 50;

/**
 * Builds the issue explorer's queries from the filters (FR-DSH-03, US-06). Every value the
 * visitor typed or picked travels as a parameter, never as SQL text. Filters combine with AND.
 * The date range is by creation date with both days included, and search needs every word to
 * appear, in any order, in the title or AI summary, ignoring case.
 */
export function buildIssueQueries(filters, sortKey = "newest", offset = 0) {
  const conditions = [];
  const params = [];
  const add = (condition, ...values) => {
    conditions.push(condition);
    params.push(...values);
  };

  if (filters.repository && filters.repository !== ALL_REPOSITORIES) {
    add("repository = ?", filters.repository);
  }
  if (filters.state) {
    add("state = ?", filters.state);
  }
  if (filters.category) {
    add("category = ?", filters.category);
  }
  if (filters.severity) {
    add("severity = ?", filters.severity);
  }
  if (parseDay(filters.from)) {
    add("created_at >= CAST(? AS DATE)", filters.from);
  }
  if (parseDay(filters.to)) {
    add("created_at < CAST(? AS DATE) + INTERVAL 1 DAY", filters.to);
  }
  for (const word of searchWords(filters.search)) {
    add("contains(lower(title || ' ' || coalesce(summary, '')), ?)", word);
  }

  const where = conditions.length > 0 ? `WHERE ${conditions.join(" AND ")}` : "";
  const orderBy = (SORTS[sortKey] ?? SORTS.newest).orderBy;
  const safeOffset = Number.isInteger(offset) && offset > 0 ? offset : 0;

  return {
    count: { sql: `SELECT CAST(count(*) AS INTEGER) AS total FROM issues ${where}`, params },
    page: {
      sql: `SELECT ${ISSUE_COLUMNS} FROM issues ${where} ORDER BY ${orderBy} LIMIT ${PAGE_SIZE} OFFSET ${safeOffset}`,
      params,
    },
  };
}

/** The search box's words, lowercased, without duplicates. */
export function searchWords(search) {
  return [...new Set((search ?? "").toLowerCase().split(/\s+/).filter(Boolean))];
}

/** The query for one filter's choices, such as every category that appears. Fields are fixed, never typed in. */
export function buildChoicesQuery(field) {
  if (!["category", "severity"].includes(field)) {
    throw new Error(`No choices for ${field}`);
  }
  return { sql: `SELECT DISTINCT ${field} AS value FROM issues WHERE ${field} IS NOT NULL ORDER BY 1`, params: [] };
}

/**
 * The weekly rows for one repository (or 'all') whose week starts inside the date range,
 * oldest first (US-02).
 */
export function buildWeeklyQuery(repository, from, to) {
  const conditions = ["repository = ?"];
  const params = [repository];
  if (parseDay(from)) {
    conditions.push("week_start >= CAST(? AS DATE)");
    params.push(from);
  }
  if (parseDay(to)) {
    conditions.push("week_start <= CAST(? AS DATE)");
    params.push(to);
  }
  return {
    sql: `SELECT CAST(epoch_ms(week_start) AS DOUBLE) AS week_ms,
            CAST(new_issues AS INTEGER) AS new_issues, CAST(closed_issues AS INTEGER) AS closed_issues,
            CAST(median_days_to_close AS DOUBLE) AS median_days_to_close
          FROM weekly WHERE ${conditions.join(" AND ")} ORDER BY week_start`,
    params,
  };
}

/** How long ago something happened, in plain words. */
export function timeAgo(date, now) {
  const minutes = Math.max(0, Math.round((now - date) / 60000));
  if (minutes < 60) {
    return minutes <= 1 ? "just now" : `${minutes} minutes ago`;
  }
  const hours = Math.round(minutes / 60);
  if (hours < 48) {
    return hours === 1 ? "1 hour ago" : `${hours} hours ago`;
  }
  return `${Math.round(hours / 24)} days ago`;
}

/** Whether the data is older than the pipeline's stale-data threshold (48 hours). */
export function isStale(updatedAt, now, hours = 48) {
  return now - updatedAt > hours * 60 * 60 * 1000;
}

/** Only links to GitHub issues are ever made clickable; anything else is shown as text. */
export function isGitHubIssueUrl(url) {
  return typeof url === "string" && /^https:\/\/github\.com\/[^/\s]+\/[^/\s]+\/issues\/\d+$/.test(url);
}

export function formatNumber(value) {
  return value == null ? "—" : Number(value).toLocaleString("en-US", { maximumFractionDigits: 1 });
}

export function formatDate(date) {
  return date == null
    ? "—"
    : date.toLocaleDateString("en-US", { timeZone: "UTC", year: "numeric", month: "short", day: "numeric" });
}

export function formatDateTime(date) {
  return date.toLocaleString("en-US", {
    timeZone: "UTC",
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
    timeZoneName: "short",
  });
}

function round1(value) {
  return Math.round(value * 10) / 10;
}
