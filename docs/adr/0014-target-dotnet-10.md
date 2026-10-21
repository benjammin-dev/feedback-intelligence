# 0014: Target .NET 10 instead of .NET 8

**Status:** Accepted
**Date:** 2026-10-19

## Context

ADR 0003 chose C# on .NET 8 because it was a long-term support (LTS) release. Support for .NET 8 ends in November 2026, within weeks of this project starting, after which it receives no security fixes. .NET 10 is the current LTS release, supported until November 2028, and the development machine already has its SDK installed.

## Decision

Target **.NET 10** (`net10.0`) for all projects. Pin the SDK in `global.json` (version 10.0.401, rolling forward to later feature bands), so local builds and CI use the same version. Everything else in ADR 0003 still applies.

## Alternatives considered

- **Stay on .NET 8:** no changes needed, but the project would run on an unsupported runtime for nearly its entire life, which conflicts with keeping dependencies patched (NFR-SEC-04).
- **.NET 9:** a standard-term release whose support also ends in November 2026, so it has the same problem.

## Consequences

- The project stays on a supported runtime until at least November 2028.
- CI installs the exact SDK from `global.json`, and Dependabot proposes package updates.
- Supersedes ADR 0003. The language choice and its reasoning are unchanged; only the runtime version moves.
