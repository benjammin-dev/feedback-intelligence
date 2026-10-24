-- 0002: Reporting tables for the Overview page, and the procedure that rebuilds them.
-- Column meanings are documented in docs/data-dictionary.md.

CREATE TABLE reporting.overview
(
    repository           varchar(201)  NOT NULL,
    period               varchar(10)   NOT NULL,
    period_start         datetime2(0)  NOT NULL,
    period_end           datetime2(0)  NOT NULL,
    new_issues           int           NOT NULL,
    closed_issues        int           NOT NULL,
    bug_count            int           NULL,
    critical_count       int           NULL,
    median_days_to_close decimal(7, 1) NULL,
    CONSTRAINT PK_overview PRIMARY KEY (repository, period),
    CONSTRAINT CK_overview_period CHECK (period IN ('current', 'previous'))
);

CREATE TABLE reporting.site_info
(
    data_updated_at   datetime2(0)   NOT NULL,
    last_run_id       bigint         NOT NULL,
    repositories      nvarchar(1000) NOT NULL,
    ai_model          varchar(100)   NULL,
    prompt_version    varchar(20)    NULL,
    ai_daily_limit    smallint       NULL,
    ai_requests_today smallint       NULL,
    CONSTRAINT CK_site_info_repositories CHECK (ISJSON(repositories) = 1)
);
GO

-- Rebuilds the reporting tables from scratch, in one transaction, so the publisher
-- never sees a half-built state.
--
-- For the walking skeleton this reads raw.issue, which is safe because only counts
-- leave the procedure. M3 changes the source to cleaned.issue.
--
-- raw.issue holds issues updated in the fetch window (90 days). Any issue created
-- or closed in the last 14 days was updated then too, so the counts are complete.
CREATE PROCEDURE reporting.rebuild
    @as_of  datetime2(0),
    @run_id bigint
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
        FROM raw.issue AS i
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

    COMMIT TRANSACTION;
END;
