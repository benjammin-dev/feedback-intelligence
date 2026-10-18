# 0003: Use C# and .NET 8 for the pipeline application

**Status:** Accepted
**Date:** 2026-10-14

## Context

The pipeline needs to call APIs, coordinate several steps, talk to SQL Server, and be thoroughly tested. It will grow over several milestones, so the code needs clear structure and strong typing to stay maintainable. The project owner is most experienced in C#.

## Decision

Write the pipeline as a C# console application on **.NET 8** (the current long-term support release), using dependency injection, configuration, and logging from `Microsoft.Extensions`.

## Alternatives considered

- **Python:** a richer ecosystem for data and AI, and Presidio is Python-native, but it offers weaker compile-time checking for a codebase of this size and isn't the owner's strongest language.
- **PowerShell or Bash scripts:** quick to start, but hard to test and structure as the system grows.

## Consequences

- Strong typing, interfaces, and mature testing tools (xUnit, Testcontainers) support the 80% coverage target.
- First-class SQL Server and Azure SDK support.
- Presidio must be called over HTTP from its container, since it isn't available as a .NET library. This is acceptable and keeps the redactor replaceable.
- The Spark prototype (US-21) will use PySpark, a deliberate exception because .NET support for Spark is no longer actively developed.
