# 0012: Develop on Windows using WSL 2 and Docker Desktop

**Status:** Accepted
**Date:** 2026-10-14

## Context

Development happens on a Windows PC. Production runs on GitHub's Ubuntu Linux runners. Local development needs SQL Server and Presidio running in containers (FR-DEV-04), and differences between local and production environments are a common source of bugs.

## Decision

Keep the repository inside a **WSL 2 Ubuntu** distribution and edit it with **VS Code** through its WSL extension. Run containers with **Docker Desktop** using the WSL 2 backend. Use **SSMS** on Windows to inspect the local SQL Server container.

## Alternatives considered

- **Native Windows development:** works, but scripts, file paths, and line endings differ from the Linux production environment, leading to "works on my machine" failures.
- **A full Linux virtual machine:** a closer match to production, but heavier, slower to set up, and harder to use alongside Windows tools.
- **GitHub Codespaces:** a cloud development environment that matches Linux exactly, but its free monthly allowance is limited.

## Consequences

- Local scripts and container setup behave the same as on GitHub's runners, so CI surprises are rare.
- Files must stay in the Linux file system, not under `/mnt/c/`, for good performance.
- Line endings need a `.gitattributes` file so Windows and Linux tools agree.
- The full Visual Studio IDE can't be used inside WSL; VS Code with the C# Dev Kit is the editor.
- Running SQL Server and Presidio locally needs about 4 GB or more of free memory.
