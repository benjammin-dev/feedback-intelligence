// Run with: node --test dashboard/tests
import assert from "node:assert/strict";
import { test } from "node:test";

import {
  buildChoicesQuery,
  buildIssueQueries,
  buildWeeklyQuery,
  describeChange,
  isGitHubIssueUrl,
  isStale,
  overviewFor,
  PAGE_SIZE,
  parseDay,
  searchWords,
  timeAgo,
} from "../js/logic.js";

const day = (value) => new Date(`${value}T00:00:00Z`);

test("describeChange says up, down, or no change in words, not only color", () => {
  assert.deepEqual(describeChange(15, 12), { direction: "up", text: "▲ Up 3 from the 7 days before (12)" });
  assert.deepEqual(describeChange(2.5, 4, " days"), {
    direction: "down",
    text: "▼ Down 1.5 days from the 7 days before (4 days)",
  });
  assert.equal(describeChange(7, 7).direction, "same");
  assert.equal(describeChange(null, 7), null);
  assert.equal(describeChange(7, null), null);
});

test("overviewFor picks the repository's rows and works out the bug share", () => {
  const rows = [
    { repository: "all", period: "current", new_issues: 20, bug_count: 5 },
    { repository: "all", period: "previous", new_issues: 0, bug_count: 0 },
    { repository: "microsoft/winget-cli", period: "current", new_issues: 10, bug_count: null },
  ];

  const all = overviewFor(rows, "all");
  assert.equal(all.current.bug_percent, 25);
  assert.equal(all.previous.bug_percent, null); // no issues, so no share
  assert.equal(overviewFor(rows, "microsoft/winget-cli").current.bug_percent, null);
  assert.equal(overviewFor(rows, "microsoft/winget-cli").previous, null);
});

test("parseDay accepts only yyyy-mm-dd", () => {
  assert.deepEqual(parseDay("2026-10-01"), day("2026-10-01"));
  assert.equal(parseDay(""), null);
  assert.equal(parseDay("10/01/2026"), null);
  assert.equal(parseDay("2026-13-45"), null);
});

test("buildIssueQueries passes every visitor value as a parameter, never as SQL", () => {
  const sneaky = "x'); DROP TABLE issues; --";
  const { count, page } = buildIssueQueries({
    repository: sneaky,
    state: "open",
    category: sneaky,
    severity: "high",
    from: "2026-10-01",
    to: "2026-10-07",
    search: `Upgrade  ${sneaky}`,
  });

  for (const query of [count, page]) {
    assert.ok(!query.sql.includes("DROP"), query.sql);
    assert.equal((query.sql.match(/\?/g) ?? []).length, query.params.length);
  }
  assert.deepEqual(count.params, [sneaky, "open", sneaky, "high", "2026-10-01", "2026-10-07", "upgrade", "x');", "drop", "table", "issues;", "--"]);
});

test("buildIssueQueries leaves out unset filters, 'all' repositories, and invalid dates", () => {
  const { count, page } = buildIssueQueries({ repository: "all", from: "10/01/2026", search: "  " });

  assert.equal(count.sql, "SELECT CAST(count(*) AS INTEGER) AS total FROM issues ");
  assert.deepEqual(count.params, []);
  assert.ok(page.sql.endsWith(`ORDER BY created_at DESC, issue_number DESC LIMIT ${PAGE_SIZE} OFFSET 0`));
});

test("buildIssueQueries accepts only known sort orders and whole-number offsets", () => {
  assert.ok(buildIssueQueries({}, "slowest", 50).page.sql.endsWith(`days_to_close DESC NULLS LAST, issue_number DESC LIMIT ${PAGE_SIZE} OFFSET 50`));
  assert.ok(buildIssueQueries({}, "1; DROP TABLE issues", -5).page.sql.endsWith(`created_at DESC, issue_number DESC LIMIT ${PAGE_SIZE} OFFSET 0`));
  assert.ok(buildIssueQueries({}, "newest", "50; DROP").page.sql.endsWith("OFFSET 0"));
});

test("searchWords lowercases and drops repeats", () => {
  assert.deepEqual(searchWords("  Upgrade upgrade CRASH "), ["upgrade", "crash"]);
  assert.deepEqual(searchWords(undefined), []);
});

test("buildChoicesQuery only accepts known fields", () => {
  assert.match(buildChoicesQuery("category").sql, /SELECT DISTINCT category/);
  assert.throws(() => buildChoicesQuery("title; DROP TABLE issues"));
});

test("buildWeeklyQuery filters one repository and optional dates", () => {
  assert.deepEqual(buildWeeklyQuery("all", "", "").params, ["all"]);
  assert.deepEqual(buildWeeklyQuery("all", "2026-08-01", "2026-10-31").params, ["all", "2026-08-01", "2026-10-31"]);
  assert.deepEqual(buildWeeklyQuery("all", "bad", "2026-10-31").params, ["all", "2026-10-31"]);
});

test("timeAgo and isStale describe the data's age", () => {
  const now = new Date("2026-11-01T20:00:00Z");
  assert.equal(timeAgo(new Date("2026-11-01T19:59:30Z"), now), "just now");
  assert.equal(timeAgo(new Date("2026-11-01T19:15:00Z"), now), "45 minutes ago");
  assert.equal(timeAgo(new Date("2026-11-01T13:17:00Z"), now), "7 hours ago");
  assert.equal(timeAgo(new Date("2026-10-29T20:00:00Z"), now), "3 days ago");
  assert.equal(isStale(new Date("2026-10-31T13:00:00Z"), now), false);
  assert.equal(isStale(new Date("2026-10-30T19:00:00Z"), now), true);
});

test("only GitHub issue links are treated as links", () => {
  assert.equal(isGitHubIssueUrl("https://github.com/microsoft/winget-cli/issues/142"), true);
  assert.equal(isGitHubIssueUrl("javascript:alert(1)"), false);
  assert.equal(isGitHubIssueUrl("https://github.com.evil.example/a/b/issues/1"), false);
  assert.equal(isGitHubIssueUrl("http://github.com/a/b/issues/1"), false);
  assert.equal(isGitHubIssueUrl(null), false);
});
