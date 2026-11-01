-- 0005: Reporting tables for the issue explorer, trends, and pipeline health, added to
-- the rebuild procedure. Column meanings are documented in docs/data-dictionary.md.

CREATE TABLE reporting.issue
(
    repository      varchar(201)   NOT NULL,
    issue_number    int            NOT NULL,
    title           nvarchar(1000) NOT NULL,
    state           varchar(10)    NOT NULL,
    state_reason    varchar(20)    NULL,
    created_at      datetime2(0)   NOT NULL,
    updated_at      datetime2(0)   NOT NULL,
    closed_at       datetime2(0)   NULL,
    days_to_close   decimal(7, 1)  NULL,
    category        varchar(20)    NULL,
    severity        varchar(10)    NULL,
    severity_reason nvarchar(300)  NULL,
    summary         nvarchar(1000) NULL,
    ai_status       varchar(15)    NOT NULL,
    html_url        varchar(300)   NOT NULL,
    CONSTRAINT PK_reporting_issue PRIMARY KEY (repository, issue_number),
    CONSTRAINT CK_reporting_issue_ai_status CHECK (ai_status IN ('classified', 'unclassified', 'pending'))
);

CREATE TABLE reporting.weekly_metrics
(
    repository           varchar(201)  NOT NULL,
    week_start           date          NOT NULL,
    new_issues           int           NOT NULL,
    closed_issues        int           NOT NULL,
    critical_count       int           NULL,
    median_days_to_close decimal(7, 1) NULL,
    CONSTRAINT PK_weekly_metrics PRIMARY KEY (repository, week_start)
);

CREATE TABLE reporting.run_health
(
    run_id              bigint      NOT NULL,
    [trigger]           varchar(10) NOT NULL,
    started_at          datetime2(0) NOT NULL,
    duration_seconds    int         NULL,
    status              varchar(10) NOT NULL,
    issues_fetched      int         NOT NULL,
    ai_requests         smallint    NOT NULL,
    ai_cache_hits       int         NOT NULL,
    issues_unclassified int         NOT NULL,
    failed_step         varchar(50) NULL,
    CONSTRAINT PK_run_health PRIMARY KEY (run_id)
);
GO

-- The rebuild procedure from migration 0003, extended with the three tables above.
CREATE OR ALTER PROCEDURE reporting.rebuild
    @as_of                datetime2(0),
    @run_id               bigint,
    @initial_history_days int = 90
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @current_start  datetime2(0) = DATEADD(day, -7, @as_of);
    DECLARE @previous_start datetime2(0) = DATEADD(day, -14, @as_of);

    BEGIN TRANSACTION;

    DELETE FROM reporting.overview;

    WITH periods AS
    (
        SELECT 'current' AS period, @current_start AS period_start, @as_of AS period_end
        UNION ALL
        SELECT 'previous', @previous_start, @current_start
    ),
    issues AS
    (
        SELECT r.owner + '/' + r.name AS repository, i.created_at, i.closed_at
        FROM cleaned.issue AS i
        JOIN cleaned.repository AS r ON r.repo_id = i.repo_id
    ),
    -- Every issue appears twice: once under its repository and once under 'all'.
    -- Medians can't be combined across repositories, so 'all' is calculated directly.
    scoped AS
    (
        SELECT repository, created_at, closed_at FROM issues
        UNION ALL
        SELECT 'all', created_at, closed_at FROM issues
    ),
    -- Every tracked repository gets rows even with no issues, so the dashboard shows 0, not a gap.
    names AS
    (
        SELECT owner + '/' + name AS repository FROM cleaned.repository
        UNION ALL
        SELECT 'all'
    ),
    counts AS
    (
        SELECT
            n.repository,
            p.period,
            p.period_start,
            p.period_end,
            SUM(CASE WHEN s.created_at >= p.period_start AND s.created_at < p.period_end THEN 1 ELSE 0 END) AS new_issues,
            SUM(CASE WHEN s.closed_at  >= p.period_start AND s.closed_at  < p.period_end THEN 1 ELSE 0 END) AS closed_issues
        FROM names AS n
        CROSS JOIN periods AS p
        LEFT JOIN scoped AS s ON s.repository = n.repository
        GROUP BY n.repository, p.period, p.period_start, p.period_end
    ),
    closed AS
    (
        SELECT s.repository, p.period, DATEDIFF(second, s.created_at, s.closed_at) / 86400.0 AS days_to_close
        FROM scoped AS s
        JOIN periods AS p ON s.closed_at >= p.period_start AND s.closed_at < p.period_end
    ),
    medians AS
    (
        SELECT DISTINCT
            repository,
            period,
            PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY days_to_close)
                OVER (PARTITION BY repository, period) AS median_days
        FROM closed
    )
    INSERT INTO reporting.overview
        (repository, period, period_start, period_end, new_issues, closed_issues,
         bug_count, critical_count, median_days_to_close)
    SELECT
        c.repository, c.period, c.period_start, c.period_end, c.new_issues, c.closed_issues,
        NULL, NULL, CAST(m.median_days AS decimal(7, 1))
    FROM counts AS c
    LEFT JOIN medians AS m ON m.repository = c.repository AND m.period = c.period;

    DELETE FROM reporting.site_info;

    INSERT INTO reporting.site_info (data_updated_at, last_run_id, repositories)
    SELECT
        @as_of,
        @run_id,
        N'[' + COALESCE(
            STRING_AGG(CAST(N'"' + STRING_ESCAPE(owner + '/' + name, 'json') + N'"' AS nvarchar(max)), N',')
                WITHIN GROUP (ORDER BY owner, name),
            N'') + N']'
    FROM cleaned.repository;

    -- ---------------------------------------------------------------------------------
    -- reporting.issue: one row per issue for the issue explorer. Only the redacted title
    -- is published; AI columns stay empty, and ai_status 'pending', until M4.
    -- ---------------------------------------------------------------------------------
    DELETE FROM reporting.issue;

    INSERT INTO reporting.issue
        (repository, issue_number, title, state, state_reason, created_at, updated_at, closed_at,
         days_to_close, category, severity, severity_reason, summary, ai_status, html_url)
    SELECT
        r.owner + '/' + r.name,
        i.issue_number,
        i.title_redacted,
        i.state,
        i.state_reason,
        i.created_at,
        i.updated_at,
        i.closed_at,
        CAST(DATEDIFF(second, i.created_at, i.closed_at) / 86400.0 AS decimal(7, 1)),
        NULL, NULL, NULL, NULL,
        'pending',
        i.html_url
    FROM cleaned.issue AS i
    JOIN cleaned.repository AS r ON r.repo_id = i.repo_id;

    -- ---------------------------------------------------------------------------------
    -- reporting.weekly_metrics: complete Monday-to-Sunday weeks for the Trends page.
    -- A repository's first week is the first full week inside its initial history window,
    -- because earlier weeks were only partly fetched and would undercount.
    -- The current, unfinished week is left out; the Overview covers the last 7 days.
    -- 1900-01-01 was a Monday, so whole weeks counted from it always start on Monday.
    -- ---------------------------------------------------------------------------------
    DELETE FROM reporting.weekly_metrics;

    DECLARE @current_week date =
        DATEADD(day, (DATEDIFF(day, '19000101', @as_of) / 7) * 7, CAST('19000101' AS date));

    WITH repositories AS
    (
        SELECT
            r.owner + '/' + r.name AS repository,
            DATEADD(day, (DATEDIFF(day, '19000101', DATEADD(day, 6 - @initial_history_days, r.onboarded_at)) / 7) * 7,
                    CAST('19000101' AS date)) AS first_week
        FROM cleaned.repository AS r
    ),
    names AS
    (
        SELECT repository, first_week FROM repositories
        UNION ALL
        SELECT 'all', MIN(first_week) FROM repositories HAVING COUNT(*) > 0
    ),
    numbers AS
    (
        -- DATEADD needs an int, and ROW_NUMBER returns a bigint, hence the CAST.
        SELECT TOP (5000) CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS int) AS n
        FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b
    ),
    weeks AS
    (
        SELECT n.repository, DATEADD(week, x.n, n.first_week) AS week_start
        FROM names AS n
        JOIN numbers AS x ON DATEADD(week, x.n, n.first_week) < @current_week
    ),
    issues AS
    (
        SELECT r.owner + '/' + r.name AS repository, i.created_at, i.closed_at
        FROM cleaned.issue AS i
        JOIN cleaned.repository AS r ON r.repo_id = i.repo_id
    ),
    scoped AS
    (
        SELECT repository, created_at, closed_at FROM issues
        UNION ALL
        SELECT 'all', created_at, closed_at FROM issues
    ),
    counts AS
    (
        SELECT
            w.repository,
            w.week_start,
            SUM(CASE WHEN s.created_at >= w.week_start AND s.created_at < DATEADD(day, 7, w.week_start) THEN 1 ELSE 0 END) AS new_issues,
            SUM(CASE WHEN s.closed_at  >= w.week_start AND s.closed_at  < DATEADD(day, 7, w.week_start) THEN 1 ELSE 0 END) AS closed_issues
        FROM weeks AS w
        LEFT JOIN scoped AS s ON s.repository = w.repository
        GROUP BY w.repository, w.week_start
    ),
    closed AS
    (
        SELECT w.repository, w.week_start, DATEDIFF(second, s.created_at, s.closed_at) / 86400.0 AS days_to_close
        FROM weeks AS w
        JOIN scoped AS s
            ON s.repository = w.repository
           AND s.closed_at >= w.week_start
           AND s.closed_at < DATEADD(day, 7, w.week_start)
    ),
    medians AS
    (
        SELECT DISTINCT
            repository,
            week_start,
            PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY days_to_close)
                OVER (PARTITION BY repository, week_start) AS median_days
        FROM closed
    )
    INSERT INTO reporting.weekly_metrics
        (repository, week_start, new_issues, closed_issues, critical_count, median_days_to_close)
    SELECT c.repository, c.week_start, c.new_issues, c.closed_issues, NULL, CAST(m.median_days AS decimal(7, 1))
    FROM counts AS c
    LEFT JOIN medians AS m ON m.repository = c.repository AND m.week_start = c.week_start;

    -- ---------------------------------------------------------------------------------
    -- reporting.run_health: the last 30 finished runs. error_message is deliberately left
    -- out; error details go only to alert issues and logs, never the dashboard.
    -- ---------------------------------------------------------------------------------
    DELETE FROM reporting.run_health;

    INSERT INTO reporting.run_health
        (run_id, [trigger], started_at, duration_seconds, status, issues_fetched, ai_requests,
         ai_cache_hits, issues_unclassified, failed_step)
    SELECT TOP (30)
        run_id, [trigger], started_at, DATEDIFF(second, started_at, finished_at), status, issues_fetched,
        ai_requests, ai_cache_hits, issues_unclassified, failed_step
    FROM ops.run
    WHERE status IN ('succeeded', 'failed')
    ORDER BY started_at DESC;

    COMMIT TRANSACTION;
END;
