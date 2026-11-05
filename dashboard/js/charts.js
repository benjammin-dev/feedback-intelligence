// Draws the Trends charts with Chart.js (loaded as a classic script, which defines window.Chart).
// Series differ by line style and point shape as well as color, so they can be told apart
// without color (NFR-USE-02), and each chart also has a data table.

import { formatDate } from "./logic.js";

const charts = new Map();

/** Draws, or redraws, a line chart of weekly new and closed issues. */
export function drawVolumeChart(canvas, weeks) {
  draw(canvas, {
    type: "line",
    data: {
      labels: weeks.map((week) => shortDate(week.week_ms)),
      datasets: [
        series(
          "New issues",
          weeks.map((week) => week.new_issues),
          "--chart-1",
          { pointStyle: "circle" },
        ),
        series(
          "Closed issues",
          weeks.map((week) => week.closed_issues),
          "--chart-2",
          {
            pointStyle: "triangle",
            borderDash: [6, 4],
          },
        ),
      ],
    },
    options: options("Issues per week"),
  });
}

/** Draws, or redraws, a bar chart of the median days to close, by week. */
export function drawMedianChart(canvas, weeks) {
  draw(canvas, {
    type: "bar",
    data: {
      labels: weeks.map((week) => shortDate(week.week_ms)),
      datasets: [
        {
          label: "Median days to close",
          data: weeks.map((week) => week.median_days_to_close),
          backgroundColor: color("--chart-1"),
          borderRadius: 3,
        },
      ],
    },
    options: options("Days"),
  });
}

function draw(canvas, config) {
  charts.get(canvas)?.destroy();
  charts.set(canvas, new window.Chart(canvas, config));
}

function series(label, data, colorVariable, extra) {
  const value = color(colorVariable);
  return {
    label,
    data,
    borderColor: value,
    backgroundColor: value,
    borderWidth: 2,
    pointRadius: 3,
    tension: 0.2,
    ...extra,
  };
}

function options(yTitle) {
  const text = color("--muted");
  const grid = color("--border");
  return {
    responsive: true,
    maintainAspectRatio: false,
    animation: false,
    interaction: { mode: "index", intersect: false },
    plugins: { legend: { labels: { color: text, usePointStyle: true } } },
    scales: {
      x: { ticks: { color: text, maxRotation: 0, autoSkipPadding: 12 }, grid: { color: grid } },
      y: {
        beginAtZero: true,
        title: { display: true, text: yTitle, color: text },
        ticks: { color: text, precision: 0 },
        grid: { color: grid },
      },
    },
  };
}

function color(variable) {
  return getComputedStyle(document.documentElement).getPropertyValue(variable).trim();
}

function shortDate(ms) {
  return formatDate(new Date(ms)).replace(/, \d{4}$/, "");
}
