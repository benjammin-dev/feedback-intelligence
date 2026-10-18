# 0007: Use Microsoft Presidio for PII redaction

**Status:** Accepted
**Date:** 2026-10-14

## Context

Issue text written by the public may contain emails, IP addresses, access tokens, names, and usernames in file paths. These must be removed before AI processing, storage in the cleaned layer, or publishing (FR-PRV-01). Detection needs to handle both fixed patterns and context-dependent data like names.

## Decision

Run **Presidio's analyzer and anonymizer** as Docker containers alongside the pipeline. The C# PII redactor calls them over HTTP and replaces each finding with a typed placeholder such as `<EMAIL>`. Add **custom recognizers** for patterns Presidio doesn't cover by default, including GitHub access tokens and usernames in Windows file paths.

## Alternatives considered

- **Regular expressions in C# only:** simple and fast, but can't reliably detect names, and becomes brittle as patterns grow.
- **Azure AI Language PII detection:** a managed service with strong detection, but requires a paid Azure subscription (ADR 0002).
- **Asking the AI model to redact:** would send raw personal data to the AI service, which is exactly what redaction is meant to prevent.

## Consequences

- Free, open-source, and well established, with detection built on patterns, checksums, and language models.
- Two extra containers add startup time to each run and memory use locally.
- Detection isn't perfect: it can miss items or flag harmless text. A test set with known PII (FR-PRV-04) guards against misses, and over-redaction is acceptable as the safer failure.
- Detection is strongest in English; non-English text may be redacted less reliably.
- The redactor sits behind a C# interface, so Presidio could be replaced with a managed service later without changing the rest of the pipeline.
