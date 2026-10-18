# 0006: Use GitHub Models for AI enrichment

**Status:** Accepted
**Date:** 2026-10-14

## Context

Issues need a category, severity, and summary, and emerging topics need short names and summaries. This requires a capable language model at $0 with no payment method on file. Only redacted text may be sent.

## Decision

Use **GitHub Models** through its OpenAI-compatible API, authenticated in GitHub Actions with the built-in `GITHUB_TOKEN`. Use a lower-tier model to get the higher free request limit. Access the model through a C# interface so the provider can be swapped.

## Alternatives considered

- **Azure OpenAI or the OpenAI API:** production-grade, but both require paid accounts.
- **Google AI Studio free tier:** generous limits, but free-tier prompts may be used to improve Google's products, which is a poor fit for user-generated content.
- **Groq free tier:** a viable free option, kept as a fallback provider (risk R1).
- **A local model on the runner:** no API limits, but runners have no GPU, so it would be too slow for the 30-minute run target.

## Consequences

- No API keys to manage or leak; the token is issued automatically per run.
- Free limits (about 150 requests per day for lower-tier models) require batching several issues per request and caching all results (FR-AI-03, FR-AI-05).
- GitHub may change or end the free tier (risk R1); the interface and cache keep the system working, with fallback providers available.
- Because the API is OpenAI-compatible, moving to Azure OpenAI later is mostly a change of endpoint and credentials.
