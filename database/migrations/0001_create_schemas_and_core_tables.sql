-- 0001: Create the four schemas and the tables the walking skeleton needs.
-- Column meanings are documented in docs/data-dictionary.md.
-- Later tables arrive in later numbered scripts; applied scripts are never edited.

CREATE SCHEMA raw;
GO
CREATE SCHEMA cleaned;
GO
CREATE SCHEMA reporting;
GO
CREATE SCHEMA ops;
GO

CREATE TABLE cleaned.repository
(
    repo_id      smallint     IDENTITY(1, 1) NOT NULL,
    owner        varchar(100) NOT NULL,
    name         varchar(100) NOT NULL,
    onboarded_at datetime2(0) NOT NULL,
    CONSTRAINT PK_repository PRIMARY KEY (repo_id),
    CONSTRAINT UQ_repository_owner_name UNIQUE (owner, name)
);

CREATE TABLE raw.issue
(
    repo_id      smallint       NOT NULL,
    issue_number int            NOT NULL,
    title        nvarchar(1000) NOT NULL,
    body         nvarchar(max)  NULL,
    state        varchar(10)    NOT NULL,
    state_reason varchar(20)    NULL,
    labels       nvarchar(2000) NOT NULL,
    created_at   datetime2(0)   NOT NULL,
    updated_at   datetime2(0)   NOT NULL,
    closed_at    datetime2(0)   NULL,
    html_url     varchar(300)   NOT NULL,
    fetched_at   datetime2(0)   NOT NULL,
    CONSTRAINT PK_raw_issue PRIMARY KEY (repo_id, issue_number),
    CONSTRAINT FK_raw_issue_repository FOREIGN KEY (repo_id) REFERENCES cleaned.repository (repo_id),
    CONSTRAINT CK_raw_issue_state CHECK (state IN ('open', 'closed')),
    CONSTRAINT CK_raw_issue_labels CHECK (ISJSON(labels) = 1)
);

CREATE TABLE ops.run
(
    run_id              bigint         NOT NULL,
    [trigger]           varchar(10)    NOT NULL,
    backfill_from       datetime2(0)   NULL,
    backfill_to         datetime2(0)   NULL,
    started_at          datetime2(0)   NOT NULL,
    finished_at         datetime2(0)   NULL,
    status              varchar(10)    NOT NULL,
    issues_fetched      int            NOT NULL CONSTRAINT DF_run_issues_fetched DEFAULT (0),
    issues_rejected     int            NOT NULL CONSTRAINT DF_run_issues_rejected DEFAULT (0),
    issues_redacted     int            NOT NULL CONSTRAINT DF_run_issues_redacted DEFAULT (0),
    ai_requests         smallint       NOT NULL CONSTRAINT DF_run_ai_requests DEFAULT (0),
    ai_cache_hits       int            NOT NULL CONSTRAINT DF_run_ai_cache_hits DEFAULT (0),
    issues_unclassified int            NOT NULL CONSTRAINT DF_run_issues_unclassified DEFAULT (0),
    failed_step         varchar(50)    NULL,
    error_message       nvarchar(2000) NULL,
    CONSTRAINT PK_run PRIMARY KEY (run_id),
    CONSTRAINT CK_run_trigger CHECK ([trigger] IN ('schedule', 'manual')),
    CONSTRAINT CK_run_status CHECK (status IN ('running', 'succeeded', 'failed'))
);
