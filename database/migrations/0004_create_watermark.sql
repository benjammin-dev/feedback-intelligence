-- 0004: How far ingestion has reached for each repository (FR-ING-02).
-- Column meanings are documented in docs/data-dictionary.md.

CREATE TABLE ops.watermark
(
    repo_id             smallint     NOT NULL,
    last_updated_at     datetime2(0) NOT NULL,
    last_success_run_id bigint       NOT NULL,
    CONSTRAINT PK_watermark PRIMARY KEY (repo_id),
    CONSTRAINT FK_watermark_repository FOREIGN KEY (repo_id) REFERENCES cleaned.repository (repo_id)
);
