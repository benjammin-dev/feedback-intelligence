-- 0003: The cleaned issue table, and reporting switched to read from it.
-- Column meanings are documented in docs/data-dictionary.md.

CREATE TABLE cleaned.issue
(
    repo_id             smallint       NOT NULL,
    issue_number        int            NOT NULL,
    title_redacted      nvarchar(1000) NOT NULL,
    body_redacted       nvarchar(4000) NULL,
    body_length         int            NOT NULL,
    body_truncated      bit            NOT NULL,
    content_hash        binary(32)     NOT NULL,
    redaction_count     smallint       NOT NULL,
    state               varchar(10)    NOT NULL,
    state_reason        varchar(20)    NULL,
    labels              nvarchar(2000) NOT NULL,
    created_at          datetime2(0)   NOT NULL,
    updated_at          datetime2(0)   NOT NULL,
    closed_at           datetime2(0)   NULL,
    html_url            varchar(300)   NOT NULL,
    first_seen_run_id   bigint         NOT NULL,
    last_changed_run_id bigint         NOT NULL,
    CONSTRAINT PK_cleaned_issue PRIMARY KEY (repo_id, issue_number),
    CONSTRAINT FK_cleaned_issue_repository FOREIGN KEY (repo_id) REFERENCES cleaned.repository (repo_id),
    CONSTRAINT CK_cleaned_issue_state CHECK (state IN ('open', 'closed')),
    CONSTRAINT CK_cleaned_issue_labels CHECK (ISJSON(labels) = 1)
);

-- Weekly metrics filter on these constantly; incremental loading depends on updated_at.
CREATE INDEX IX_cleaned_issue_created_at ON cleaned.issue (created_at);
CREATE INDEX IX_cleaned_issue_updated_at ON cleaned.issue (updated_at);
GO

-- Same procedure as migration 0002, now reading the cleaned layer (ADR 0009).
-- Once history is restored (M3), cleaned.issue holds every issue ever seen, not just
-- this run's fetch, so the counts no longer depend on the fetch window.
CREATE OR ALTER PROCEDURE reporting.rebuild
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

    COMMIT TRANSACTION;
END;
