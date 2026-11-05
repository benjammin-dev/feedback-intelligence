# 0016: Validate the config file with the pipeline's settings classes

**Status:** Accepted
**Date:** 2026-11-05

## Context

FR-CFG-02 requires the config file to be validated in CI. The design planned a JSON Schema file, `config/pipeline.schema.json`, checked in CI and at startup. By M3 the pipeline already had C# settings classes with a `FindProblems` method that checks every value's allowed range at startup. A JSON Schema would describe the same rules a second time, in a second language.

## Decision

The C# settings classes are the schema. A test in CI loads the real `config/pipeline.json` with the same .NET configuration code the pipeline uses, with one stricter rule: any setting the classes don't know, such as a misspelled name, is an error. It then runs `FindProblems`. CI runs this test as its own named step, "Validate config", so a bad config change fails a clearly labeled check.

## Alternatives considered

- **JSON Schema (the original plan):** gives editor autocomplete and is language-neutral, but the ranges and rules would be written twice, and the two copies could drift apart without any test noticing.
- **Validation at startup only:** catches the same problems, but only on the next scheduled run, after a bad change has already merged.

## Consequences

- One definition of every setting and its limits, used both at startup and in CI.
- Misspelled settings, which .NET otherwise ignores silently, fail the pull request.
- Editors don't offer autocomplete for the config file. At its current size, about 20 settings, that's acceptable.
- Settings supplied only through environment variables, such as the backfill dates, are checked at startup, not by the CI test.
